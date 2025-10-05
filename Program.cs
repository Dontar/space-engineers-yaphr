using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk preserve
        const string screenTag = "{Yaphr}";
        const string craneGroup = "{Yaphr} Crane";
        #endregion

        const string RotationIndicatorY = "Yaw";
        const string RotationIndicatorX = "Pitch";
        const string RollIndicator = "Roll";
        const string MoveIndicatorX = "Left/Right";
        const string MoveIndicatorY = "Up/Down";
        const string MoveIndicatorZ = "Forward/Backward";

        readonly CraneControlMenuManager menuSystem;

        string Mode = "off";
        string Profile = "default";
        TaskManager.ITask _LogoTask;
        TaskManager.ITask _ControlTask;
        TaskManager.ITask _ParkTask;
        TaskManager.ITask _WorkTask;

        IEnumerable<IMyShipController> Controllers => Memo.Of("ControlCrane", 100, () => Util.GetBlocks<IMyShipController>(b => Me.IsSameConstructAs(b) && b.CanControlShip));

        MyIni Config => Memo.Of("GetConfig", Me.CustomData, () =>
        {
            var myIni = new MyIni();
            myIni.TryParse(Me.CustomData);
            return myIni;
        });

        IEnumerable<IMyTextSurface> Screens => Memo.Of("Screens", 100, () => Util.GetScreens(screenTag));

        IEnumerable<PistonMotorWrapper> Sections => Memo.Of("Sections", new object[] { Config, Profile }, () =>
        {
            var opts = new List<MyIniKey>();
            Config.GetKeys(opts);
            if (opts.Count == 0) return new PistonMotorWrapper[] { };
            return opts.Select(k =>
            {
                var p = k.Section.Split('/');
                var profile = p.Length < 2 ? "default" : p.First();
                var section = p.Last();
                return new { k.Name, Section = section, Profile = profile, Value = Config.Get(k).ToString() };
            })
            .Where(i => i.Profile == Profile)
            .GroupBy(
                iniKey => iniKey.Section,
                theIniKey => theIniKey,
                (section, theIniKey) => new PistonMotorWrapper(section, theIniKey.ToDictionary(k => k.Name, k => k.Value)))
            .ToArray();
        });

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            Util.Init(this);
            menuSystem = new CraneControlMenuManager(this);
            TaskManager.RunTask(Util.StatusMonitorTask(this));
            TaskManager.RunTask(RenderMenuTask()).Every(1.7f);
            _ControlTask = TaskManager.RunTask(ControlCraneTask()).Pause();
            _ParkTask = TaskManager.RunTask(PositionCraneTask("Park")).Pause();
            _WorkTask = TaskManager.RunTask(PositionCraneTask("Work")).Pause();
            _LogoTask = TaskManager.RunTask(Util.DisplayLogo("Y.A.P.H.R", Me.GetSurface(0)));
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrEmpty(argument))
                ProcessCommands(argument);

            if (!updateSource.HasFlag(UpdateType.Update10)) return;

            if (Controllers.Count() == 0)
            {
                Util.Echo("No controller found.");
                return;
            }

            _ControlTask.Pause(Mode != "control");
            _ParkTask.Pause(Mode != "park");
            _WorkTask.Pause(Mode != "work");

            TaskManager.Tick(Runtime.TimeSinceLastRun);
        }

        void ProcessCommands(string command)
        {
            var cmd = command.ToLower().Trim();
            Stop();
            switch (cmd)
            {
                case "toggle":
                    Mode = Mode != "control" ? "control" : "off";
                    LockAndPowerOff(false);
                    break;
                case "set_park":
                case "set_work":
                    TaskManager.RunTask(SavePositionsTask(char.ToUpper(cmd[4]) + cmd.Substring(5))).Once();
                    break;
                case "park":
                case "work":
                    Mode = cmd;
                    LockAndPowerOff(false);
                    break;
                default:
                    if (cmd.StartsWith("add"))
                    {
                        AddProfile(command.Substring(4).Trim());
                    }
                    else
                    if (cmd.StartsWith("set"))
                    {
                        Profile = command.Substring(4).Trim();
                    }
                    else
                    if (menuSystem.ProcessMenuCommands(cmd))
                    {
                        foreach (var s in Screens)
                        {
                            menuSystem.Render(s);
                        }
                    }
                    break;
            }
        }

        IEnumerable ControlCraneTask()
        {
            while (true)
            {
                var controller = Controllers.FirstOrDefault(c => c.IsUnderControl);
                foreach (var descriptor in Sections)
                {
                    var direction = ReadControllerValue(controller, descriptor.OP);
                    descriptor.Control(direction);
                }
                yield return null;
            }
        }

        IEnumerable PositionCraneTask(string position)
        {
            while (true)
            {
                if (Sections.Select(d => d.Position(position)).ToArray().All(d => d))
                {
                    Mode = position == "Park" ? "off" : "control";
                    Stop();
                    LockAndPowerOff(Mode == "off");
                }
                yield return null;
            }
        }

        IEnumerable SavePositionsTask(string optName)
        {
            foreach (var info in Sections)
            {
                if (info.Blocks.Length == 0) continue;
                var ini = Config;
                var descriptor = PistonMotorUtil.Get(info.Blocks.First());
                ini.Set($"{Profile}/{info.Section}", optName, $"15/{info.PIDTune[1]}/{info.PIDTune[2]}/{info.PIDTune[3]}/{descriptor.Position}");
            }
            Me.CustomData = Config.ToString();
            yield return null;
        }

        private IEnumerable RenderMenuTask()
        {
            var pbScreen = Me.GetSurface(0);
            while (true)
            {
                foreach (var s in Screens)
                {
                    if (s == pbScreen) _LogoTask.Pause();
                    menuSystem.Render(s);
                }
                yield return null;
            }
        }

        float ReadControllerValue(IMyShipController controller, string name)
        {
            if (controller == null) return 0;
            switch (name)
            {
                case MoveIndicatorX:
                    return controller.MoveIndicator.X;
                case MoveIndicatorY:
                    return controller.MoveIndicator.Y;
                case MoveIndicatorZ:
                    return controller.MoveIndicator.Z;
                case RollIndicator:
                    return controller.RollIndicator;
                case RotationIndicatorY:
                    return controller.RotationIndicator.Y;
                case RotationIndicatorX:
                    return controller.RotationIndicator.X;
                default:
                    return 0;
            }
        }

        private void AddProfile(string v)
        {
            var opts = new List<MyIniKey>();
            Config.GetKeys(opts);

            var p = opts.Select(k =>
            {
                var t = k.Section.Split('/');
                var profile = t.Length < 2 ? "default" : t.First();
                var section = t.Last();
                return new { k.Name, Section = section, Profile = profile, Value = Config.Get(k).ToString() };
            });

            if (p.Any(k => k.Profile == v)) return;

            foreach (var i in p.Where(k => k.Profile == "default"))
            {
                Config.Set($"{v}/{i.Section}", i.Name, i.Value);
                Me.CustomData = Config.ToString();
            }
        }

        void Stop()
        {
            foreach (var s in Sections)
            {
                s.Reset();
                Array.ForEach(s.Blocks, b => PistonMotorUtil.Set(b, 0f));
            }
        }

        void LockAndPowerOff(bool locked)
        {
            foreach (var s in Sections)
            {
                s.Reset();
                Array.ForEach(s.Blocks, b => PistonMotorUtil.SetEnabledAndLock(b, locked));
            }
        }
    }
}
