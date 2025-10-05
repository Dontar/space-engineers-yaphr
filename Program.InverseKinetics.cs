using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        IEnumerable<IMyTerminalBlock> MyIKBlocks;

        PistonMotorWrapper baseRotor;
        PistonMotorWrapper baseHinge;
        PistonMotorWrapper elbowHinge;
        PistonMotorWrapper wristHinge;
        PistonMotorWrapper wristRotor;
        IMyTerminalBlock Head;

        void SetupIK()
        {
            // 1. Get all blocks in the crane group
            MyIKBlocks = Util.GetGroupOrBlocks<IMyTerminalBlock>(craneGroup);
            if (MyIKBlocks.Count() == 0) return;

            baseRotor = new PistonMotorWrapper(MyIKBlocks.OfType<IMyMotorStator>().Where(b => b.CustomName.EndsWith("Base")).ToArray());
            baseHinge = new PistonMotorWrapper(MyIKBlocks.OfType<IMyMotorAdvancedStator>().Where(b => b.CustomName.EndsWith("Base")).ToArray());
            elbowHinge = new PistonMotorWrapper(MyIKBlocks.OfType<IMyMotorAdvancedStator>().Where(b => b.CustomName.EndsWith("Elbow")).ToArray());
            wristHinge = new PistonMotorWrapper(MyIKBlocks.OfType<IMyMotorAdvancedStator>().Where(b => b.CustomName.EndsWith("Hand")).ToArray());
            wristRotor = new PistonMotorWrapper(MyIKBlocks.OfType<IMyMotorStator>().Where(b => b.CustomName.EndsWith("Hand")).ToArray());
            Head = MyIKBlocks.OfType<IMyShipConnector>().FirstOrDefault() ?? MyIKBlocks.OfType<IMyLandingGear>().FirstOrDefault() as IMyShipConnector;
        }

        bool HasToMove(IMyShipController controller, out Vector3D toPosition, double speed = 1.0)
        {
            var currentPosition = Head.GetPosition();
            if (controller == null || Vector3D.IsZero(controller.MoveIndicator))
            {
                toPosition = currentPosition;
                return false;
            }
            var move = controller.MoveIndicator;
            var matrix = controller.WorldMatrix;
            var delta = Vector3D.TransformNormal(move, matrix) * speed * Runtime.TimeSinceLastRun.TotalSeconds;
            toPosition = currentPosition + delta;
            return true;
        }

        /// <summary>
        /// Attempts to solve the IK for the crane to reach targetPosition.
        /// Returns true if a solution was found and applied.
        /// </summary>
        bool SolveIK(Vector3D targetWorld)
        {
            var resultList = new List<bool>();
            // 1. Get base position and orientation
            if (
                baseRotor == null
                || baseHinge == null
                || elbowHinge == null
                || wristHinge == null
                || wristRotor == null
                || Head == null
            ) return false;

            var baseMatrix = baseRotor.Blocks[0].CubeGrid.WorldMatrix;
            var basePos = baseMatrix.Translation;

            // 2. Compute yaw (baseRotor): angle from base to target in XZ plane
            Vector3D toTarget = targetWorld - basePos;
            var yaw = (float)Math.Atan2(toTarget.X, toTarget.Z);
            resultList.Add(baseRotor.Position(yaw));

            // 3. Transform target into the arm's local plane (after yaw)
            var yawMatrix = MatrixD.CreateRotationY(-yaw);
            var localTarget = Vector3D.TransformNormal(toTarget, yawMatrix);
            localTarget.X = 0;

            // 4. Planar 2-segment IK (shoulder pitch + elbow + pistons)
            var baseHingePosition = baseHinge.Blocks[0].GetPosition();
            var elbowHingePosition = elbowHinge.Blocks[0].GetPosition();
            var wristHingePosition = wristHinge.Blocks[0].GetPosition();
            var l1 = (elbowHingePosition - baseHingePosition).Length(); // upper arm length
            var l2 = (wristHingePosition - elbowHingePosition).Length(); // forearm length
            var d = Math.Min(localTarget.Length(), l1 + l2);

            // Law of cosines for angles
            var cosA = (l1 * l1 + d * d - l2 * l2) / (2 * l1 * d);
            var cosB = (l1 * l1 + l2 * l2 - d * d) / (2 * l1 * l2);

            var angleShoulder = (float)(Math.Acos(MathHelper.Clamp(cosA, -1, 1)) + Math.Atan2(localTarget.Y, localTarget.Z));
            var angleElbow = (float)(Math.PI - Math.Acos(MathHelper.Clamp(cosB, -1, 1)));

            // 5. Set joint targets
            resultList.Add(baseHinge.Position(angleShoulder));
            resultList.Add(elbowHinge.Position(angleElbow));

            // 6. Set piston lengths (simple proportional for now)
            // piston1.Velocity = (l1 - piston1.CurrentPosition) * 0.5f;
            // piston2.Velocity = (l2 - piston2.CurrentPosition) * 0.5f;

            // 7. Set wrist and hand to keep orientation "down"
            // Calculate the current arm direction in world space
            var armDir = Vector3D.Normalize(toTarget);

            // Desired hand "down" direction (world down)
            var baseDown = -baseMatrix.Up;

            // Calculate the rotation needed at the wrist hinge to align the hand with world down
            // Project armDir and worldDown onto the plane perpendicular to the arm's forward axis
            var baseForward = baseMatrix.Forward;
            var projWorldDown = baseDown - baseForward * baseDown.Dot(baseForward);

            // Calculate the angle between the arm's current "down" and the desired world down
            var wristPitch = (float)Math.Acos(MathHelper.Clamp(armDir.Dot(projWorldDown), -1, 1));

            // Set wrist hinge to correct for pitch
            resultList.Add(wristHinge.Position(wristPitch));

            // For wrist rotor (roll), you may want to keep a fixed roll or align with a reference
            var handForward = Head.WorldMatrix.Forward;
            var projHandForward = Vector3D.ProjectOnPlane(ref handForward, ref baseDown);
            var wristRoll = Vector3D.Angle(projHandForward, baseForward);
            resultList.Add(wristRotor.Position((float)wristRoll));

            return resultList.All(r => r);
        }
    }
}
