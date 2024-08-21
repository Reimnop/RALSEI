using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ralsei.Avalonia.Models;
using Ralsei.Avalonia.Services;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Mathematics;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering;
using Ralsei.Rendering.OpenGLES;
using Ralsei.Resources.Compressed;

namespace Ralsei.Avalonia.Export;

/// <summary>
/// What to make a video of.
/// </summary>
/// <param name="Level">The level. It is read again for the video, so the player is free to do something else.</param>
/// <param name="OutputPath">The video file.</param>
/// <param name="StartTime">Where in the song the video starts, in seconds.</param>
/// <param name="Duration">How long the video is, in seconds.</param>
/// <param name="Seed">What the random numbers of the level start from, which the same video needs.</param>
public sealed record ExportRequest(
    LevelInfo Level,
    string OutputPath,
    int Width,
    int Height,
    int Framerate,
    double StartTime,
    double Duration,
    ulong Seed,
    bool EnablePostProcessing,
    bool EnableTextRendering,
    string FFmpegPath,
    string Arguments)
{
    public int FrameCount => Math.Max(1, (int) Math.Round(Duration * Framerate));
}

/// <param name="Frame">How many frames are done.</param>
/// <param name="TotalFrames">How many frames there are.</param>
/// <param name="Elapsed">How long it has been going on.</param>
public readonly record struct ExportProgress(int Frame, int TotalFrames, TimeSpan Elapsed)
{
    public double Fraction => TotalFrames == 0 ? 0 : (double) Frame / TotalFrames;

    public double FramesPerSecond => Elapsed.TotalSeconds > 0.5 ? Frame / Elapsed.TotalSeconds : 0;

    public TimeSpan? Remaining => Frame > 0 && FramesPerSecond > 0
        ? TimeSpan.FromSeconds((TotalFrames - Frame) / FramesPerSecond)
        : null;
}

/// <summary>
/// Something went wrong that can be said in a sentence.
/// </summary>
public sealed class ExportException(string message, string? details = null, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>
    /// What FFmpeg said, if it is what failed.
    /// </summary>
    public string? Details { get; } = details;
}

/// <summary>
/// Makes a video of a level: renders it frame by frame, at any size and any speed, and gives the frames to FFmpeg
/// together with the song.
/// </summary>
public sealed class FFmpegExporter(LogStore logStore, ILogger<FFmpegExporter> logger)
{
    // Frames that are being rendered, or waiting for FFmpeg, or being written to it
    private const int FramesInFlight = 4;

    // What FFmpeg says that is kept to say what went wrong
    private const int KeptLogLines = 12;

    public Task RunAsync(ExportRequest request, IProgress<ExportProgress> progress, CancellationToken cancellationToken)
        => Task.Factory.StartNew(
            () => Run(request, progress, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private void Run(ExportRequest request, IProgress<ExportProgress> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ffmpeg = FFmpegLocator.Find(request.FFmpegPath)
                     ?? throw new ExportException("FFmpeg was not found", "Choose where it is in the settings.");

        if (request.Width < 16 || request.Height < 16)
            throw new ExportException("The video is too small");

        var directory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        logger.LogInformation(
            "Exporting '{Title}' to {Path}: {Width}x{Height} at {Framerate} fps, {Frames} frames",
            request.Level.Title, request.OutputPath, request.Width, request.Height, request.Framerate, request.FrameCount);

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.Services.AddSingleton<ILoggerProvider>(logStore);
        });

        services.AddPAS(x => x.AddCompressedResources())
            .UseOpenGLESRenderer();

        services.AddSingleton(new ExportSurfaceSettings(new Vector2i(request.Width, request.Height)));
        services.AddScoped<IOpenGLSurface>(sp => new OffscreenSurface(
            sp.GetRequiredService<OpenGLSettings>(),
            sp.GetRequiredService<ExportSurfaceSettings>()));

        Process? process = null;
        var completed = false;
        try
        {
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var sp = scope.ServiceProvider;

            // The context is made when the surface is, and the renderer creates what it needs in it. The scope frees
            // all of it, in the context, when it ends.
            var surface = (OffscreenSurface) sp.GetRequiredService<IOpenGLSurface>();
            var renderer = sp.GetRequiredService<IRenderer>();
            var factory = provider.GetRequiredService<IRenderingFactory>();
            var director = sp.GetRequiredService<AppDirector>();

            sp.GetRequiredService<RandomSeedService>().Seed = request.Seed;
            director.EnablePostProcessing = request.EnablePostProcessing;
            director.EnableTextRendering = request.EnableTextRendering;

            using (var stream = new BufferedStream(File.OpenRead(request.Level.BeatmapPath), 1 << 20))
                sp.GetRequiredService<BeatmapService>().LoadBeatmap(stream, request.Level.Format);

            // The meshes and texts that were asked for are made by this thread, it is the render thread
            factory.FlushFrame(renderer);

            cancellationToken.ThrowIfCancellationRequested();

            var log = new Queue<string>();
            process = StartFFmpeg(ffmpeg, request, log);

            RenderFrames(request, progress, cancellationToken, process, log, surface, director, factory, renderer);
            completed = true;
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                    TryKill(process);
                process.Dispose();
            }

            if (!completed)
                TryDelete(request.OutputPath);
        }
    }

    private void RenderFrames(
        ExportRequest request,
        IProgress<ExportProgress> progress,
        CancellationToken cancellationToken,
        Process process,
        Queue<string> log,
        OffscreenSurface surface,
        AppDirector director,
        IRenderingFactory factory,
        IRenderer renderer)
    {
        var frameSize = request.Width * request.Height * 4;
        var frameCount = request.FrameCount;

        // Rendering the next frame goes on while the last ones are written. Frames go round through these.
        var free = new BlockingCollection<byte[]>(FramesInFlight);
        var full = new BlockingCollection<byte[]>(FramesInFlight);
        for (var i = 0; i < FramesInFlight; i++)
            free.Add(new byte[frameSize]);

        Exception? writeFailure = null;
        var writer = Task.Factory.StartNew(() =>
        {
            try
            {
                var input = process.StandardInput.BaseStream;
                foreach (var frame in full.GetConsumingEnumerable())
                {
                    input.Write(frame, 0, frameSize);
                    free.Add(frame);
                }

                input.Flush();
                input.Close();
            }
            catch (Exception e)
            {
                writeFailure = e;
                // Whatever is waiting for a frame to use or to write is let go
                free.CompleteAdding();
            }
        }, TaskCreationOptions.LongRunning);

        using var cancellation = cancellationToken.Register(() => TryKill(process));

        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] buffer;
                try
                {
                    buffer = free.Take();
                }
                catch (InvalidOperationException)
                {
                    // The writer could not go on
                    break;
                }

                surface.Target = buffer;
                director.SubmitFrame((float) (request.StartTime + i / (double) request.Framerate));
                factory.FlushFrame(renderer);

                full.Add(buffer);

                if (clock.Elapsed - lastReport > TimeSpan.FromMilliseconds(100) || i == frameCount - 1)
                {
                    lastReport = clock.Elapsed;
                    progress.Report(new ExportProgress(i + 1, frameCount, clock.Elapsed));
                }
            }
        }
        finally
        {
            full.CompleteAdding();
            writer.Wait();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (writeFailure is not null)
        {
            // FFmpeg stopped reading, it says why when it exits
            process.WaitForExit(TimeSpan.FromSeconds(5));
            throw Failed(process, log, writeFailure);
        }

        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw Failed(process, log, null);

        progress.Report(new ExportProgress(frameCount, frameCount, clock.Elapsed));
        logger.LogInformation("Exported {Frames} frames in {Time:0.0} s", frameCount, clock.Elapsed.TotalSeconds);
    }

    private Process StartFFmpeg(string ffmpeg, ExportRequest request, Queue<string> log)
    {
        var startInfo = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-nostats", "-loglevel", "warning",

            // The frames, which come from here
            "-f", "rawvideo",
            "-pix_fmt", "rgba",
            "-s", $"{request.Width}x{request.Height}",
            "-framerate", request.Framerate.ToString(CultureInfo.InvariantCulture),
            "-i", "pipe:0",

            // The song, from where the video starts
            "-ss", request.StartTime.ToString("0.######", CultureInfo.InvariantCulture),
            "-i", request.Level.AudioPath,

            "-map", "0:v:0",
            "-map", "1:a:0",

            // What was read from the renderer has the bottom row first
            "-vf", "vflip",

            // The song is longer than what is rendered
            "-t", request.Duration.ToString("0.######", CultureInfo.InvariantCulture)
        };

        arguments.AddRange(ArgumentSplitter.Split(request.Arguments));
        arguments.Add(request.OutputPath);

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        logger.LogInformation("Starting FFmpeg: {Command}", string.Join(' ', arguments.Select(x => x.Contains(' ') ? $"\"{x}\"" : x)));

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new ExportException("FFmpeg could not be started");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new ExportException("FFmpeg could not be started", e.Message, e);
        }

        void Keep(string? line)
        {
            if (line is null)
                return;

            lock (log)
            {
                log.Enqueue(line);
                while (log.Count > KeptLogLines)
                    log.Dequeue();
            }

            logger.LogWarning("FFmpeg: {Line}", line);
        }

        process.OutputDataReceived += (_, e) => Keep(e.Data);
        process.ErrorDataReceived += (_, e) => Keep(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static ExportException Failed(Process process, Queue<string> log, Exception? cause)
    {
        string details;
        lock (log)
            details = string.Join(Environment.NewLine, log);

        var message = process.HasExited
            ? $"FFmpeg stopped with the error code {process.ExitCode}"
            : "FFmpeg stopped reading the video";

        return new ExportException(message, details.Length > 0 ? details : cause?.Message, cause);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It was gone already
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not worth a second error
        }
    }
}
