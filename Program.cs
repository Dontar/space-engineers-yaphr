using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk preserve
        const string screenTag = "{Yaphr}";
        const string craneGroup = "{Yaphr} Crane";

        const string RotationIndicatorY = "Yaw";
        const string RotationIndicatorX = "Pitch";
        const string RollIndicator = "Roll";
        const string MoveIndicatorX = "Left/Right";
        const string MoveIndicatorY = "Up/Down";
        const string MoveIndicatorZ = "Forward/Backward";
        #endregion

        CraneControlMenuManager menuSystem;
        string Mode = "off";
        bool KeepAlign = false;
        string Profile = "default";
        ITask _LogoTask;
        ITask _ControlTask;
        ITask _ParkTask;
        ITask _WorkTask;
        MyIni Config = new MyIni();
        MyCommandLine Cmd = new MyCommandLine();

        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            Util.Init(this);
            menuSystem = new CraneControlMenuManager(this);
            Task.RunTask(Util.StatusMonitorTask(this));
            Task.RunTask(RenderMenuTask()).Every(1.7f);
            _ControlTask = Task.RunTask(ControlCraneTask()).Pause();
            _ParkTask = Task.RunTask(PositionCraneTask("Park")).Pause();
            _WorkTask = Task.RunTask(PositionCraneTask("Work")).Pause();
            _LogoTask = Task.RunTask(Util.DisplayLogo("Y.A.P.H.R", Me.GetSurface(0)));
        }

        public void Main(string argument, UpdateType updateSource) {
            if (!string.IsNullOrEmpty(argument))
                ExecuteCommands(argument);

            if (!updateSource.HasFlag(UpdateType.Update10)) return;

            if (Controllers.Count() == 0) {
                Util.Echo("No controller found.");
                return;
            }

            Memo.Of("OnCustomDataChanged", Me.CustomData, () => {
                Config.TryParse(Me.CustomData);
            });

            _ControlTask.Pause(Mode != "control");
            _ParkTask.Pause(Mode != "park");
            _WorkTask.Pause(Mode != "work");

            Task.Tick(Runtime.TimeSinceLastRun);
        }

        void ExecuteCommands(string argument) {
            var cmd = Cmd;
            if (cmd.TryParse(argument)) {
                var command = cmd.Argument(0).ToLower();
                Stop();
                switch (command) {
                    case "toggle":
                        Mode = Mode != "control" ? "control" : "off";
                        LockAndPowerOff(false);
                        break;
                    case "align":
                        KeepAlign = !KeepAlign;
                        break;
                    case "set_park":
                    case "set_work":
                        var info = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
                        SavePositions(info.ToTitleCase(command.Substring(4)));
                        break;
                    case "park":
                    case "work":
                        Mode = command;
                        LockAndPowerOff(false);
                        break;
                    case "add":
                        AddProfile(cmd.Argument(1));
                        break;
                    case "set":
                        Profile = cmd.Argument(1);
                        break;
                    default:
                        if (menuSystem.ExecuteMenuCommands(cmd)) {
                            foreach (var s in Screens) {
                                menuSystem.Render(s);
                            }
                        }
                        break;
                }
            }
        }

        IEnumerable<IMyShipController> Controllers => Memo.Of("Controllers_RefreshOn", 100, () => Util.GetBlocks<IMyShipController>(b => Me.IsSameConstructAs(b) && b.CanControlShip));

        IEnumerable<IMyTextSurface> Screens => Memo.Of("Screens_RefreshOn", 100, () => Util.GetScreens(screenTag));

        IEnumerable<PistonMotorWrapper> Sections => Memo.Of("Sections_OnConfigOrProfileChanged", new object[] { Me.CustomData, Profile }, () => {
            var opts = new List<MyIniKey>();
            Config.GetKeys(opts);
            if (opts.Count == 0) return new PistonMotorWrapper[] { };
            return opts.Select(k => {
                var p = k.Section.Split('/');
                var Profile = p.Length < 2 ? "default" : p.First();
                var Section = p.Last();
                return new { k.Name, Section, Profile, Value = Config.Get(k).ToString() };
            })
            .Where(i => i.Profile == Profile)
            .GroupBy(
                iniKey => iniKey.Section,
                theIniKey => theIniKey,
                (section, theIniKey) => new PistonMotorWrapper(section, theIniKey.ToDictionary(k => k.Name, k => k.Value)))
            .ToArray();
        });

        IMyTerminalBlock BaseBlock => Memo.Of("BaseBlock", Me.CustomData, () => {
            var blocks = Util.GetGroup<IMyMechanicalConnectionBlock>(craneGroup);
            return blocks.FirstOrDefault(b => !blocks.Any(o => o.TopGrid == b.CubeGrid));
        });

        IEnumerable ControlCraneTask() {
            while (true) {
                var controller = Controllers.FirstOrDefault(c => c.IsUnderControl);
                foreach (var descriptor in Sections) {
                    if (KeepAlign && descriptor.KeepAlignedTo != "None") {
                        var dirVector = BaseBlock.WorldMatrix.GetDirectionVector((Base6Directions.Direction)Enum.Parse(typeof(Base6Directions.Direction), descriptor.KeepAlignedTo));
                        descriptor.SetPosition(dirVector);
                        continue;
                    }
                    var direction = ReadControllerValue(controller, descriptor.OP);
                    descriptor.Control(direction);
                }
                yield return null;
            }
        }

        IEnumerable PositionCraneTask(string position) {
            while (true) {
                if (Sections.Select(d => d.SetPosition(position)).ToArray().All(d => d)) {
                    Mode = position == "Park" ? "off" : "control";
                    Stop();
                    LockAndPowerOff(Mode == "off");
                }
                yield return null;
            }
        }

        void SavePositions(string optName) {
            var ini = Config;
            foreach (var info in Sections) {
                if (info.Blocks.Length == 0) continue;
                ini.Set($"{Profile}/{info.Section}", optName, $"15/{info.PIDTune[1]}/{info.PIDTune[2]}/{info.PIDTune[3]}/{info.Position}");
            }
            Me.CustomData = ini.ToString();
        }

        IEnumerable RenderMenuTask() {
            var pbScreen = Me.GetSurface(0);
            while (true) {
                var pauseLogo = false;
                foreach (var s in Screens) {
                    if (s == pbScreen) pauseLogo = true;
                    menuSystem.Render(s);
                }
                _LogoTask.Pause(pauseLogo);
                yield return null;
            }
        }

        float ReadControllerValue(IMyShipController controller, string name) {
            if (controller == null) return 0;
            switch (name) {
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

        void AddProfile(string v) {
            var opts = new List<MyIniKey>();
            Config.GetKeys(opts);

            var p = opts.Select(k => {
                var t = k.Section.Split('/');
                var profile = t.Length < 2 ? "default" : t.First();
                var section = t.Last();
                return new { k.Name, Section = section, Profile = profile, Value = Config.Get(k).ToString() };
            });

            if (p.Any(k => k.Profile == v)) return;

            foreach (var i in p.Where(k => k.Profile == "default")) {
                Config.Set($"{v}/{i.Section}", i.Name, i.Value);
                Me.CustomData = Config.ToString();
            }
        }

        void Stop() {
            foreach (var s in Sections) {
                s.Reset();
                s.SetSpeed(0);
            }
        }

        void LockAndPowerOff(bool locked) {
            foreach (var s in Sections) {
                s.Reset();
                s.SetEnabledAndLock(locked);
            }
        }
    }
}
