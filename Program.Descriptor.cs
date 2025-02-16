using Sandbox.ModAPI.Ingame;

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
    }
}
