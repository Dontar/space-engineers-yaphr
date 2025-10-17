using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static readonly string[] InputNames = new string[] { RotationIndicatorX, RotationIndicatorY, RollIndicator, MoveIndicatorX, MoveIndicatorY, MoveIndicatorZ };
        static string[] Directions = Array.ConvertAll(Base6Directions.EnumDirections, i => i.ToString()).Prepend("None").ToArray();
        class PistonMotorWrapper : PID
        {
            public IMyTerminalBlock[] Blocks;
            public Dictionary<string, string> INI;
            public string OP;
            public double DesiredVelocity = 0;
            public double[] PIDTune;
            public string Section;
            public string KeepAlignedTo;

            public PistonMotorWrapper(string section, Dictionary<string, string> ini) : base(0, 0, 0, 1d / 6d, 0) {
                var op = ini.FirstOrDefault(o => InputNames.Contains(o.Key) && o.Value != "0");

                Blocks = Util.GetGroupOrBlocks<IMyTerminalBlock>(section).ToArray();
                INI = ini;
                Section = section;
                OP = op.Key ?? "None";
                double.TryParse(op.Value, out DesiredVelocity);

                PIDTune = ini.GetValueOrDefault("Tuning", "0/15/0/2").Split('/').Select(double.Parse).ToArray();
                KeepAlignedTo = ini.GetValueOrDefault("KeepAlignedTo", "None");
                Tune(PIDTune);
            }

            public PistonMotorWrapper(IMyTerminalBlock[] blocks) : base(0, 0, 0, 1d / 60d, 0) {
                Blocks = blocks;
                Tune(new[] { 0d, 15d, 0d, 2d });
            }

            public void UpdateIni(string Profile, MyIni ini) {
                INI.Remove(OP);
                INI.Add(OP, DesiredVelocity.ToString());
                INI.ToList().ForEach(k => {
                    ini.DeleteSection(Section);
                    ini.Set($"{Profile}/{Section}", k.Key, InputNames.Contains(k.Key) && k.Key != OP ? "0" : k.Value);
                });
                ini.Set($"{Profile}/{Section}", "Tuning", string.Join("/", PIDTune));
                ini.Set($"{Profile}/{Section}", "KeepAlignedTo", KeepAlignedTo);
            }

            public void Control(float direction) {
                if (Blocks.Length == 0) return;
                var time = Task.CurrentTaskLastRun.TotalSeconds;
                var block = Blocks.First();
                var targetVelocity = MathHelper.Clamp(DesiredVelocity, -Max, Max);

                var error = targetVelocity * direction - Current;

                var output = (float)Math.Round(Signal(error, time, PIDTune), 3);
                SetSpeed(output);
            }

            public bool SetPosition(string position) {
                if (!INI.ContainsKey(position) || Blocks.Length == 0) return true;
                var time = Task.CurrentTaskLastRun.TotalSeconds;
                var value = INI[position].Split('/');
                var desiredPos = value.Skip(4).Select(float.Parse).FirstOrDefault();
                var tune = value.Take(4).Select(double.Parse).ToArray();
                var block = Blocks.First();

                var error = !IsPiston(block) ? MathHelper.WrapAngle(desiredPos - Position) : desiredPos - Position;

                var output = (float)Math.Round(Signal(error, time, tune), 3);
                SetSpeed(output);
                return Math.Abs(error) < 0.01;
            }

            public bool SetPosition(float position) {
                if (Blocks.Length == 0) return true;
                var time = Task.CurrentTaskLastRun.TotalSeconds;
                var desiredPos = position;
                var block = Blocks.First();

                var error = !IsPiston(block) ? MathHelper.WrapAngle(desiredPos - Position) : desiredPos - Position;

                var output = (float)Math.Round(Signal(error, time), 3);
                SetSpeed(output);
                return Math.Abs(error) < 0.01;
            }

            public bool SetPosition(Vector3D position) {
                if (Blocks.Length == 0) return true;
                var block = Blocks.First();
                if (IsPiston(block)) return true;

                var desiredPos = IsHinge(block)
                    ? VectorToHinge(position, (IMyMotorStator)block)
                    : VectorToRotor(position, (IMyMotorStator)block);

                var error = MathHelper.ToDegrees(MathHelper.WrapAngle(desiredPos - Position));

                var time = Task.CurrentTaskLastRun.TotalSeconds;
                var output = (float)Math.Round(Signal(error, time, new[] { 8d, 0, 0, 0 }), 3);
                SetSpeed(output);
                return Math.Abs(error) < 0.01;
            }

            float VectorToHinge(Vector3D v, IMyMotorStator block) {
                var m = block.WorldMatrix;
                var hingeUp = m.Up;
                var proj = Vector3D.Normalize(Vector3D.ProjectOnPlane(ref v, ref hingeUp));
                return (float)Math.Atan2(proj.Dot(m.Forward), proj.Dot(m.Left));
            }

            float VectorToRotor(Vector3D v, IMyMotorStator block) {
                var m = block.WorldMatrix;
                var rotorUp = m.Up;
                var proj = Vector3D.Normalize(Vector3D.ProjectOnPlane(ref v, ref rotorUp));
                return (float)Math.Atan2(proj.Dot(m.Left), proj.Dot(m.Backward));
            }

            public void SetSpeed(float speed) {
                foreach (var block in Blocks) {
                    if (block is IMyExtendedPistonBase) {
                        var piston = block as IMyExtendedPistonBase;
                        piston.Velocity = speed;
                        continue;
                    }
                    var rotor = block as IMyMotorStator;
                    rotor.TargetVelocityRPM = speed;
                }
            }

            float Max => IsPiston(Blocks.First()) ? 5.0f : 30.0f;
            float Current {
                get {
                    var block = Blocks.First();
                    return IsPiston(block) ? (block as IMyExtendedPistonBase).Velocity : (block as IMyMotorStator).TargetVelocityRPM;
                }
            }
            public float Position {
                get {
                    var block = Blocks.First();
                    return IsPiston(block)
                        ? (block as IMyExtendedPistonBase).CurrentPosition
                        : (block as IMyMotorStator).Angle;
                }
            }

            public void SetEnabledAndLock(bool locked) {
                foreach (var block in Blocks) {
                    if (block is IMyMotorStator) {
                        var rotor = block as IMyMotorStator;
                        rotor.RotorLock = locked;
                    }
                    (block as IMyFunctionalBlock).Enabled = !locked;
                }
            }

            bool IsPiston(IMyTerminalBlock block) {
                return block is IMyExtendedPistonBase;
            }

            bool IsHinge(IMyTerminalBlock block) {
                return block is IMyMotorStator && block.BlockDefinition.SubtypeName.Contains("Hinge");
            }
        }
    }
}
