using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Rendering;
using Ralsei.Util;

namespace Swag.Desktop.FFmpeg;

public class FFmpegFrameGenerator(
    IServiceProvider serviceProvider,
    FFmpegSettings settings,
    ILogger<FFmpegFrameGenerator> logger) : IDisposable
{
    private readonly StreamWriter ffmpegLogWriter = new("ffmpeg_output.log");
    
    public void Dispose()
    {
        ffmpegLogWriter.Flush();
        ffmpegLogWriter.Dispose();
    }

    public void GenerateFrames(
        string beatmapPath, string audioPath, int framerate, string outputPath,
        ulong? seed, bool enablePostProcessing, bool enableTextRendering,
        float startTime = 0f, float? duration = null)    
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        
        var beatmapService = sp.GetRequiredService<BeatmapService>();
        beatmapService.LoadBeatmapFromPath(beatmapPath);
        
        var rss = sp.GetRequiredService<RandomSeedService>();
        rss.Seed = seed ?? NumberUtil.SplitMix64((ulong)DateTimeOffset.Now.ToUnixTimeSeconds());
        
        using var audioPlayer = AudioPlayer.Load(audioPath);
        
        var totalLength = (float)audioPlayer.Length;
        if (startTime < 0 || startTime > totalLength)
            throw new ArgumentOutOfRangeException(nameof(startTime), $"Start time must be between 0 and {totalLength}");
        
        var renderDuration = duration ?? (totalLength - startTime);

        if (startTime + renderDuration > totalLength)
            renderDuration = totalLength - startTime;
        
        var frames = serviceProvider.GetRequiredService<IFrameQueue>();
        var renderer = sp.GetRequiredService<IRenderer>();
        var frameSource = sp.GetRequiredService<IFrameSource>();

        var windowSize = frameSource.RenderSize;
        
        var processStartInfo = new ProcessStartInfo(settings.ExecPath) 
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var processStartArgs = new List<string> {
            "-y",

            "-f", "rawvideo",
            "-pix_fmt", "rgba",
            "-s", $"{windowSize.X}x{windowSize.Y}",
            "-r", framerate.ToString(),
            "-i", "pipe:0",

            "-ss", startTime.ToString(CultureInfo.InvariantCulture),
            "-c:a", "libvorbis",
            "-i", audioPath,

            // cap output duration explicitly
            "-t", renderDuration.ToString(CultureInfo.InvariantCulture)
        };

        foreach (var arg in processStartArgs)
            processStartInfo.ArgumentList.Add(arg);

        foreach (var arg in CommandLineParser.SplitCommandLine(settings.Args))
            processStartInfo.ArgumentList.Add(arg);
        processStartInfo.ArgumentList.Add(outputPath);
        
        using var ffmpegProcess = Process.Start(processStartInfo);
        if (ffmpegProcess == null)
            throw new InvalidOperationException("Failed to start FFmpeg process");
        
        ffmpegProcess.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                ffmpegLogWriter.WriteLine(args.Data);
        };
        
        ffmpegProcess.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                ffmpegLogWriter.WriteLine(args.Data);
        };
        
        ffmpegProcess.BeginOutputReadLine();
        ffmpegProcess.BeginErrorReadLine();
        
        logger.LogInformation("Started FFmpeg process with PID {PID}", ffmpegProcess.Id);
        
        logger.LogInformation("Rendering video to {OutputPath}", outputPath);
        
        var appDirector = sp.GetRequiredService<AppDirector>();
        appDirector.EnablePostProcessing = enablePostProcessing;
        appDirector.EnableTextRendering = enableTextRendering;

        // Resources are created asynchronously, and this thread is the render thread. Run the requests made
        // so far (the beatmap's meshes and texts) now, or the first frames would be drawn without them.
        frames.FlushFrame(renderer);

        var frameCount = (int)(renderDuration * framerate);
        for (var i = 0; i < frameCount; i++)
        {
            var time = startTime + i / (float)framerate;
            appDirector.SubmitFrame(time, windowSize);
            frames.FlushFrame(renderer);

            var frameData = frameSource.FrameData;
            ffmpegProcess.StandardInput.BaseStream.Write(frameData);
            
            RenderProgress(i + 1, frameCount);
        }
        
        ffmpegProcess.StandardInput.Close();
        ffmpegProcess.WaitForExit();
        
        logger.LogInformation("Finished rendering video");
    }
    
    private static void RenderProgress(int frame, int totalFrames)
    {
        const int barWidth = 20;

        var bar = $"[{new string('#', (int)(frame / (float)totalFrames * barWidth)),-barWidth}] " +
                  $"{frame}/{totalFrames} " +
                  $"({frame / (float)totalFrames * 100:0.00}%)";

        Console.Write("\e[2K");
        Console.WriteLine(bar);
        
        Console.Write("\e[1A");
    }
}