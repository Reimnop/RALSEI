using Pamx.Keyframes;

namespace Swag.Service;

public delegate float EaseFunction(float t);

public static class EaseFunctions
{
	private static readonly EaseFunction[] easeFunctions =
	[
		Linear,
		Instant,
		SineIn,
		SineOut,
		SineInOut,
		ElasticIn,
		ElasticOut,
		ElasticInOut,
		BackIn,
		BackOut,
		BackInOut,
		BounceIn,
		BounceOut,
		BounceInOut,
		QuadIn,
		QuadOut,
		QuadInOut,
		CircIn,
		CircOut,
		CircInOut,
		ExpoIn,
		ExpoOut,
		ExpoInOut,
	];

	public static EaseFunction? Get(Ease name)
	{
		var index = (int)name;
		if (index < 0 || index >= easeFunctions.Length)
			return null;
		return easeFunctions[index];
	}
	
	public static EaseFunction GetOrLinear(Ease name)
		=> Get(name) ?? Linear;

	private const float Pi = 3.14159265359f;
	private const float Pi2 = Pi / 2.0f;
	private const float B1 = 1.0f / 2.75f;
	private const float B2 = 2.0f / 2.75f;
	private const float B3 = 1.5f / 2.75f;
	private const float B4 = 2.5f / 2.75f;
	private const float B5 = 2.25f / 2.75f;
	private const float B6 = 2.625f / 2.75f;

	/// <summary>Ease a value to its target and then back. Use this to wrap another easing function.</summary>
	public static EaseFunction ToAndFro(EaseFunction easeFunction)
	{
		return t => ToAndFro(easeFunction(t));
	}

	/// <summary>Ease a value to its target and then back.</summary>
	public static float ToAndFro(float t)
	{
		return t < 0.5f ? t * 2 : 1 + ((t - 0.5f) / 0.5f) * -1;
	}

	public static float Linear(float t)
		=> t;

	public static float Instant(float t)
	{
		return 0.0f;
	}

	#region Sine

	public static float SineIn(float t)
	{
		if (t == 1) return 1;
		return -MathF.Cos(Pi2 * t) + 1;
	}

	public static float SineOut(float t)
	{
		return MathF.Sin(Pi2 * t);
	}

	public static float SineInOut(float t)
	{
		return -MathF.Cos(Pi * t) / 2 + 0.5f;
	}

	#endregion

	#region Elastic

	public static float ElasticIn(float t)
	{
		return (MathF.Sin(13 * Pi2 * t) * MathF.Pow(2, 10 * (t - 1)));
	}

	public static float ElasticOut(float t)
	{
		if (t == 1) return 1;
		return (MathF.Sin(-13 * Pi2 * (t + 1)) * MathF.Pow(2, -10 * t) + 1);
	}

	public static float ElasticInOut(float t)
	{
		if (t < 0.5)
		{
			return (0.5f * MathF.Sin(13 * Pi2 * (2 * t)) * MathF.Pow(2, 10 * ((2 * t) - 1)));
		}

		return (0.5f * (MathF.Sin(-13 * Pi2 * ((2 * t - 1) + 1)) * MathF.Pow(2, -10 * (2 * t - 1)) + 2));
	}

	#endregion

	#region Back

	public static float BackIn(float t)
	{
		return (t * t * (2.70158f * t - 1.70158f));
	}

	public static float BackOut(float t)
	{
		return (1 - (--t) * (t) * (-2.70158f * t - 1.70158f));
	}

	public static float BackInOut(float t)
	{
		t *= 2;
		if (t < 1) return (t * t * (2.70158f * t - 1.70158f) / 2);
		t--;
		return ((1 - (--t) * (t) * (-2.70158f * t - 1.70158f)) / 2 + .5f);
	}

	#endregion

	#region Bounce

	public static float BounceIn(float t)
	{
		t = 1 - t;
		if (t < B1) return (1 - 7.5625f * t * t);
		if (t < B2) return (1 - (7.5625f * (t - B3) * (t - B3) + .75f));
		if (t < B4) return (1 - (7.5625f * (t - B5) * (t - B5) + .9375f));
		return (1 - (7.5625f * (t - B6) * (t - B6) + .984375f));
	}

	public static float BounceOut(float t)
	{
		if (t < B1) return (7.5625f * t * t);
		if (t < B2) return (7.5625f * (t - B3) * (t - B3) + .75f);
		if (t < B4) return (7.5625f * (t - B5) * (t - B5) + .9375f);
		return (7.5625f * (t - B6) * (t - B6) + .984375f);
	}

	public static float BounceInOut(float t)
	{
		if (t < .5)
		{
			t = 1 - t * 2;
			if (t < B1) return ((1 - 7.5625f * t * t) / 2);
			if (t < B2) return ((1 - (7.5625f * (t - B3) * (t - B3) + .75f)) / 2);
			if (t < B4) return ((1 - (7.5625f * (t - B5) * (t - B5) + .9375f)) / 2);
			return ((1 - (7.5625f * (t - B6) * (t - B6) + .984375f)) / 2);
		}

		t = t * 2 - 1;
		if (t < B1) return ((7.5625f * t * t) / 2 + .5f);
		if (t < B2) return ((7.5625f * (t - B3) * (t - B3) + .75f) / 2 + .5f);
		if (t < B4) return ((7.5625f * (t - B5) * (t - B5) + .9375f) / 2 + .5f);
		return ((7.5625f * (t - B6) * (t - B6) + .984375f) / 2 + .5f);
	}

	#endregion

	#region Quad

	public static float QuadIn(float t)
	{
		return (t * t);
	}

	public static float QuadOut(float t)
	{
		return (-t * (t - 2));
	}

	public static float QuadInOut(float t)
	{
		return (t <= .5 ? t * t * 2 : 1 - (--t) * t * 2);
	}

	#endregion

	#region Circ

	public static float CircIn(float t)
	{
		return (-(MathF.Sqrt(1 - t * t) - 1));
	}

	public static float CircOut(float t)
	{
		return (MathF.Sqrt(1 - (t - 1) * (t - 1)));
	}

	public static float CircInOut(float t)
	{
		return (t <= .5 ? (MathF.Sqrt(1 - t * t * 4) - 1) / -2 : (MathF.Sqrt(1 - (t * 2 - 2) * (t * 2 - 2)) + 1) / 2);
	}

	#endregion

	#region Expo

	public static float ExpoIn(float t)
	{
		return (MathF.Pow(2, 10 * (t - 1)));
	}

	public static float ExpoOut(float t)
	{
		if (t == 1) return 1;
		return (-MathF.Pow(2, -10 * t) + 1);
	}

	public static float ExpoInOut(float t)
	{
		if (t == 1) return 1;
		return (t < .5 ? MathF.Pow(2, 10 * (t * 2 - 1)) / 2 : (-MathF.Pow(2, -10 * (t * 2 - 1)) + 2) / 2);
	}

	#endregion
}