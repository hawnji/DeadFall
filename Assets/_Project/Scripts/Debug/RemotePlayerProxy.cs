using System;
using Deadfall.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Deadfall.DebugTools
{
    /// <summary>
    /// How another player looks on this machine during the networking spike: a coloured capsule with a
    /// head and a gun barrel that follows their aim. It has no colliders, so it never blocks shots or
    /// movement. Positions are smoothed toward the latest state; real interpolation arrives with M11.
    /// </summary>
    public sealed class RemotePlayerProxy : MonoBehaviour, ICombatTarget, IDamageable
    {
        private const float PositionSharpness = 18f;
        private const float RotationSharpness = 22f;
        private const float SnapDistance = 5f;
        private const float StandEye = 1.68f;
        private const float CrouchEye = 1.05f;

        private Transform _body;
        private Transform _aim;
        private Renderer[] _renderers;
        private Vector3 _targetPos;
        private float _targetYaw, _targetPitch;
        private float _yaw, _pitch;
        private bool _crouched, _dead;
        private bool _hasState;
        private float _eyeHeight = StandEye;

        public ulong ClientId { get; private set; }
        public float Health01 { get; private set; } = 1f;
        public bool IsDead => _dead;

        /// <summary>Host: an enemy hit this player. The amount is sent to their machine, which applies it.</summary>
        public event Action<RemotePlayerProxy, float, Vector3> DamageRequested;

        // Host enemies hunt remote players through these (ICombatTarget / IDamageable).
        public Transform TargetTransform => transform;
        public bool IsTargetable => _hasState && !_dead && isActiveAndEnabled;
        public IDamageable Damageable => this;
        public bool IsAlive => !_dead;
        public float DamageReduction => 0f; // the owner applies their own reduction

        public float ApplyDamage(in DamageInfo info)
        {
            if (_dead || info.Amount <= 0f) return 0f;
            DamageRequested?.Invoke(this, info.Amount, info.Direction);
            return info.Amount;
        }

        private void OnEnable() => CombatTargets.Register(this);
        private void OnDisable() => CombatTargets.Unregister(this);
        /// <summary>Latest reported eye position (unsmoothed), used by the host to validate hit claims.</summary>
        public Vector3 EyePosition => _targetPos + Vector3.up * (_crouched ? CrouchEye : StandEye);

        public static RemotePlayerProxy Create(ulong clientId, Transform parent)
        {
            var root = new GameObject($"RemotePlayer_{clientId}");
            root.transform.SetParent(parent, false);
            Color color = ColorFor(clientId);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            SandboxBootstrap.Tint(body, color);

            var aim = new GameObject("Aim").transform;
            aim.SetParent(root.transform, false);
            aim.localPosition = new Vector3(0f, StandEye, 0f);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Object.Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(aim, false);
            head.transform.localScale = Vector3.one * 0.34f;
            SandboxBootstrap.Tint(head, color * 0.8f);

            GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            Object.Destroy(visor.GetComponent<Collider>());
            visor.transform.SetParent(aim, false);
            visor.transform.localPosition = new Vector3(0f, 0.02f, 0.15f);
            visor.transform.localScale = new Vector3(0.26f, 0.08f, 0.08f);
            SandboxBootstrap.Tint(visor, new Color(0.1f, 0.1f, 0.12f));

            GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gun.name = "Gun";
            Object.Destroy(gun.GetComponent<Collider>());
            gun.transform.SetParent(aim, false);
            gun.transform.localPosition = new Vector3(0.25f, -0.25f, 0.35f);
            gun.transform.localScale = new Vector3(0.08f, 0.1f, 0.6f);
            SandboxBootstrap.Tint(gun, new Color(0.2f, 0.2f, 0.22f));

            var proxy = root.AddComponent<RemotePlayerProxy>();
            proxy.ClientId = clientId;
            proxy._body = body.transform;
            proxy._aim = aim;
            proxy._renderers = root.GetComponentsInChildren<Renderer>();
            return proxy;
        }

        private static Color ColorFor(ulong id)
        {
            Color[] palette =
            {
                new Color(0.25f, 0.55f, 0.95f), new Color(0.95f, 0.6f, 0.2f),
                new Color(0.6f, 0.35f, 0.9f), new Color(0.3f, 0.8f, 0.75f),
            };
            return palette[(int)(id % (ulong)palette.Length)];
        }

        public void SetTarget(Vector3 position, float yaw, float pitch, bool crouched, bool dead, float health01)
        {
            _targetPos = position;
            _targetYaw = yaw;
            _targetPitch = pitch;
            _crouched = crouched;
            _dead = dead;
            Health01 = Mathf.Clamp01(health01);
            if (!_hasState || (transform.position - position).sqrMagnitude > SnapDistance * SnapDistance)
            {
                transform.position = position;
                _yaw = yaw;
                _pitch = pitch;
                _hasState = true;
            }
        }

        public void Tick(float dt)
        {
            if (!_hasState) return;
            float kp = 1f - Mathf.Exp(-PositionSharpness * dt);
            float kr = 1f - Mathf.Exp(-RotationSharpness * dt);
            transform.position = Vector3.Lerp(transform.position, _targetPos, kp);
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, kr);
            _pitch = Mathf.Lerp(_pitch, _targetPitch, kr);
            _eyeHeight = Mathf.Lerp(_eyeHeight, _crouched ? CrouchEye : StandEye, kp);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _aim.localPosition = new Vector3(0f, _eyeHeight, 0f);
            _aim.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            float bodyScale = _eyeHeight / StandEye;
            _body.localScale = new Vector3(0.7f, 0.9f * bodyScale, 0.7f);
            _body.localPosition = new Vector3(0f, 0.9f * bodyScale, 0f);

            // Dead players lie down until they respawn.
            if (_dead) transform.rotation = Quaternion.Euler(-80f, _yaw, 0f);
        }

        private void OnGUI()
        {
            Camera cam = Camera.main;
            if (cam == null || _renderers == null) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * (_eyeHeight + 0.45f));
            if (sp.z <= 0f || sp.z > 60f) return;
            string label = $"Player {ClientId}  {Mathf.RoundToInt(Health01 * 100f)}%{(_dead ? "  DOWN" : "")}";
            GUI.Label(new Rect(sp.x - 70f, Screen.height - sp.y - 10f, 140f, 20f), label);
        }
    }
}
