// using Knighter.Entities;
using Redungeon.Core.Abstractions;
using Microsoft.Xna.Framework;

namespace Redungeon.Core.Graphics;

public class LightManager : Component
{
	public readonly List<Light> Lights;

	public LightManager() =>
		Lights = new List<Light>();

	public Light AddLight(Color color, float radius, float intencity = 1f, Entity target = null) {
        Light light = new Light {
            Color = color,
            Radius = radius,
            TargetRadius = radius,
            Intencity = intencity,
            TargetIntencity = intencity
        };
        light.Follow(target);
		if (target != null)
			light.Position = target.WorldCenter;
		Lights.Add(light);
		return light;
	}

	public override void Update() {
		Lights.RemoveAll((Light l) => l.Dead);
		foreach (Light light in Lights)
			light.Update();
		base.Update();
	}

	public void Clear() =>
		Lights.Clear();
}
