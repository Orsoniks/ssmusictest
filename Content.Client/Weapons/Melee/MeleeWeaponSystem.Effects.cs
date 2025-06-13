using System.Numerics;
using Content.Client.Animations;
using Content.Client.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Client.Weapons.Melee;

public sealed partial class MeleeWeaponSystem
{
    private const string FadeAnimationKey = "melee-fade";
    private const string SlashAnimationKey = "melee-slash";
    private const string ThrustAnimationKey = "melee-thrust";
    [Dependency] private readonly IRobustRandom _robustRandom = default!;

    private const float SlashLength = 0.35f;
    private const float SlashAngle = 0.7f;

    /// <summary>
    /// Does all of the melee effects for a player that are predicted, i.e. character lunge and weapon animation.
    /// </summary>
    public override void DoLunge(EntityUid user, EntityUid weapon, Angle angle, Vector2 localPos, string? animation, bool predicted = true)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        var lunge = GetLungeAnimation(localPos);

        // Stop any existing lunges on the user.
        _animation.Stop(user, MeleeLungeKey);
        _animation.Play(user, lunge, MeleeLungeKey);

        if (localPos == Vector2.Zero || animation == null)
            return;

        if (!_xformQuery.TryGetComponent(user, out var userXform) || userXform.MapID == MapId.Nullspace)
            return;

        var animationUid = Spawn(animation, userXform.Coordinates);

        if (!TryComp<SpriteComponent>(animationUid, out var sprite) || !TryComp<WeaponArcVisualsComponent>(animationUid, out var arcComponent))
        {
            return;
        }

        var swingAnimationTimeMultiplier = 1f;

        var spriteRotation = Angle.Zero;
        if (arcComponent.Animation != WeaponArcAnimation.None && TryComp(weapon, out MeleeWeaponComponent? meleeWeaponComponent))
        {
            if (user != weapon
                && TryComp(weapon, out SpriteComponent? weaponSpriteComponent))
                _sprite.CopySprite((weapon, weaponSpriteComponent), (animationUid, sprite));

            spriteRotation = meleeWeaponComponent.WideAnimationRotation;
            swingAnimationTimeMultiplier = meleeWeaponComponent.AttackRate;

            if (meleeWeaponComponent.SwingLeft)
                angle *= -1;

            if (_robustRandom.NextFloat() < 0.35f)
                angle *= -1;
        }
        _sprite.SetRotation((animationUid, sprite), localPos.ToWorldAngle());
        var distance = Math.Clamp(localPos.Length() / 2f, 0.2f, 1f);

        var xform = _xformQuery.GetComponent(animationUid);
        TrackUserComponent track;

        switch (arcComponent.Animation)
        {
            case WeaponArcAnimation.Slash:
                track = EnsureComp<TrackUserComponent>(animationUid);
                track.User = user;
                _animation.Play(animationUid, GetSlashAnimation(sprite, angle, spriteRotation, swingAnimationTimeMultiplier), SlashAnimationKey);

                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, 0f, SlashLength / swingAnimationTimeMultiplier), FadeAnimationKey);

                break;
            case WeaponArcAnimation.Thrust:
                track = EnsureComp<TrackUserComponent>(animationUid);
                track.User = user;
                _animation.Play(animationUid, GetThrustAnimation((animationUid, sprite), distance, spriteRotation), ThrustAnimationKey);

                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, 0.05f, 0.15f), FadeAnimationKey);

                break;
            case WeaponArcAnimation.None:
                var (mapPos, mapRot) = TransformSystem.GetWorldPositionRotation(userXform);
                var worldPos = mapPos + (mapRot - userXform.LocalRotation).RotateVec(localPos);
                var newLocalPos = Vector2.Transform(worldPos, TransformSystem.GetInvWorldMatrix(xform.ParentUid));
                TransformSystem.SetLocalPositionNoLerp(animationUid, newLocalPos, xform);

                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, 0f, 0.15f), FadeAnimationKey);

                break;
        }
    }
    /// <summary>
    /// Returns an vector rotated by vector a to b by factor + 90 degrees, for use in slash animation
    /// </summary>
    private Vector2 SwingOffsetByLerpAngle(Angle a, Angle b, float factor)
    {
        return Angle.Lerp(a + 90f, b + 90f, factor).RotateVec(new Vector2(0f, -1f));
    }
    private Animation GetSlashAnimation(SpriteComponent sprite, Angle arc, Angle spriteRotation, float attackTime)
    {
        float endTime = SlashLength / attackTime;
        float length = endTime + 0.05f;

        var startRotation = sprite.Rotation + arc * SlashAngle - 90f;
        var endRotation = sprite.Rotation - arc * SlashAngle - 90f;

        return new Animation()
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, -0.5f) + spriteRotation + 90f, 0f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 0.4f) + spriteRotation + 90f, 0.1f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 1.2f) + spriteRotation + 90f, 0.2f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 1.8f) + spriteRotation + 90f, 0.4f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 2.2f) + spriteRotation + 90f, 0.6f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 2.5f) + spriteRotation + 90f, endTime)
                    }
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(SwingOffsetByLerpAngle(startRotation, endRotation, -0.1f), 0f),
                        new AnimationTrackProperty.KeyFrame(SwingOffsetByLerpAngle(startRotation, endRotation, 0.6f), 0.1f),
                        new AnimationTrackProperty.KeyFrame(SwingOffsetByLerpAngle(startRotation, endRotation, 0.9f), 0.2f),
                        new AnimationTrackProperty.KeyFrame(SwingOffsetByLerpAngle(startRotation, endRotation, 1.1f), 0.3f),
                        new AnimationTrackProperty.KeyFrame(SwingOffsetByLerpAngle(startRotation, endRotation, 1.5f), 0.6f),
                        new AnimationTrackProperty.KeyFrame(SwingOffsetByLerpAngle(startRotation, endRotation, 2f), endTime),
                    }
                },
            }
        };
    }

    private Animation GetThrustAnimation(Entity<SpriteComponent> sprite, float distance, Angle spriteRotation)
    {
        const float thrustEnd = 0.05f;
        const float length = 0.15f;
        var startOffset = sprite.Comp.Rotation.RotateVec(new Vector2(0f, -distance / 5f));
        var endOffset = sprite.Comp.Rotation.RotateVec(new Vector2(0f, -distance));
        _sprite.SetRotation(sprite.AsNullable(), sprite.Comp.Rotation + spriteRotation);

        return new Animation()
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startOffset, 0f),
                        new AnimationTrackProperty.KeyFrame(endOffset, thrustEnd),
                        new AnimationTrackProperty.KeyFrame(endOffset, length),
                    }
                },
            }
        };
    }

    private Animation GetFadeAnimation(SpriteComponent sprite, float start, float end)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(end + 0.5f),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(sprite.Color, start),
                        new AnimationTrackProperty.KeyFrame(sprite.Color, float.Lerp(start, end, 0.5f)),
                        new AnimationTrackProperty.KeyFrame(sprite.Color.WithAlpha(0f), end)
                    }
                }
            }
        };
    }

    /// <summary>
    /// Get the sprite offset animation to use for mob lunges.
    /// </summary>
    private Animation GetLungeAnimation(Vector2 direction)
    {
        const float length = 0.1f;

        return new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(direction.Normalized() * 0.15f, 0f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, length)
                    }
                }
            }
        };
    }

    /// <summary>
    /// Updates the effect positions to follow the user
    /// </summary>
    private void UpdateEffects()
    {
        var query = EntityQueryEnumerator<TrackUserComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var arcComponent, out var xform))
        {
            if (arcComponent.User == null)
                continue;

            Vector2 targetPos = TransformSystem.GetWorldPosition(arcComponent.User.Value);

            if (arcComponent.Offset != Vector2.Zero)
            {
                var entRotation = TransformSystem.GetWorldRotation(xform);
                targetPos += entRotation.RotateVec(arcComponent.Offset);
            }

            TransformSystem.SetWorldPosition(uid, targetPos);
        }
    }
}
