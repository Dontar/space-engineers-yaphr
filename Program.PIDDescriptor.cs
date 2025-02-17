using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        struct Descriptor
        {
            public float Max;
            public float Current;
            public float Position;
            public static Descriptor Get(IMyTerminalBlock block)
            {
                return block is IMyExtendedPistonBase
                ? new Descriptor() { Max = 5.0f, Current = (block as IMyExtendedPistonBase).Velocity, Position = (block as IMyExtendedPistonBase).CurrentPosition }
                : new Descriptor() { Max = 30.0f, Current = (block as IMyMotorStator).TargetVelocityRPM, Position = (block as IMyMotorStator).Angle };
            }
            public static void Set(IMyTerminalBlock block, float speed)
            {
                if (block is IMyExtendedPistonBase)
                {
                    var piston = block as IMyExtendedPistonBase;
                    piston.Velocity = speed;
                    return;
                }
                var rotor = block as IMyMotorStator;
                rotor.TargetVelocityRPM = speed;
            }
        }

        public static readonly string[] InputNames = new string[] { RotationIndicatorX, RotationIndicatorY, RollIndicator, MoveIndicatorX, MoveIndicatorY, MoveIndicatorZ };
        class PIDDescriptor : PID
        {
            public IMyTerminalBlock[] Blocks;
            public Dictionary<string, string> INI;
            public string OP;
            public double DesiredVelocity = 0;
            public double[] PIDTune;
            public string Section;

            public PIDDescriptor(string section, Dictionary<string, string> ini) : base(0, 0, 0, 1d / 6d, 0)
            {
                var op = ini.FirstOrDefault(o => InputNames.Contains(o.Key) && o.Value != "0");

                Blocks = Util.GetGroupOrBlocks<IMyTerminalBlock>(section).ToArray();
                INI = ini;
                Section = section;
                OP = op.Key ?? "None";
                double.TryParse(op.Value, out DesiredVelocity);

                PIDTune = ini.GetValueOrDefault("Tuning", "0/15/0/2").Split('/').Select(double.Parse).ToArray();
                Tune(PIDTune);
            }

            public void UpdateIni(string Profile, MyIni ini)
            {
                INI.Remove(OP);
                INI.Add(OP, DesiredVelocity.ToString());
                INI.ToList().ForEach(k =>
                {
                    ini.DeleteSection(Section);
                    ini.Set($"{Profile}/{Section}", k.Key, InputNames.Contains(k.Key) && k.Key != OP ? "0" : k.Value);
                });
                ini.Set($"{Profile}/{Section}", "Tuning", string.Join("/", PIDTune));
            }

            public void Control(float direction)
            {
                if (Blocks.Length == 0) return;
                var time = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var block = Blocks.First();
                var velocityState = Descriptor.Get(block);
                var targetVelocity = MathHelper.Clamp(DesiredVelocity, -velocityState.Max, velocityState.Max);

                var error = targetVelocity * direction - velocityState.Current;

                var output = (float)Math.Round(Signal(error, time, PIDTune), 3);
                Array.ForEach(Blocks, b => Descriptor.Set(b, output));
            }

            public bool Position(string position)
            {
                if (!INI.ContainsKey(position) || Blocks.Length == 0) return true;
                var time = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var value = INI[position].Split('/');
                var desiredPos = value.Skip(4).Select(float.Parse).FirstOrDefault();
                var tune = value.Take(4).Select(double.Parse).ToArray();
                var block = Blocks.First();
                var positionState = Descriptor.Get(block);

                var error = (block is IMyMotorStator) ? MathHelper.WrapAngle(desiredPos - positionState.Position) : desiredPos - positionState.Position;

                var output = (float)Math.Round(Signal(error, time, tune), 3);
                Array.ForEach(Blocks, b => Descriptor.Set(b, output));
                return Math.Abs(error) < 0.01;
            }
        }
    }
}
