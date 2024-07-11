using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program
    {
        class PIDDescriptor : PID
        {
            public List<IMyTerminalBlock> blocks;
            public Dictionary<string, string> ini;
            public string op;
            public double desiredVelocity = 0;
            public double[] tune;
            public string section;
            public PIDDescriptor(string section, Dictionary<string, string> ini) : base(0, 0, 0, 1d / 6d, 0)
            {
                var op = ini.FirstOrDefault(o => InputNames.Contains(o.Key) && o.Value != "0");

                blocks = Util.GetGroupOrBlocks<IMyTerminalBlock>(section).ToList();
                this.ini = ini;
                this.section = section;
                this.op = op.Key ?? "None";
                double.TryParse(op.Value, out desiredVelocity);

                tune = ini.GetValueOrDefault("Tuning", "0/15/0/2").Split('/').Select(double.Parse).ToArray();
                Tune(tune);
            }
            public void UpdateIni(string Profile, MyIni ini)
            {
                this.ini.Remove(op);
                this.ini.Add(op, desiredVelocity.ToString());
                this.ini.ToList().ForEach(k =>
                {
                    ini.DeleteSection(section);
                    ini.Set($"{Profile}/{section}", k.Key, InputNames.Contains(k.Key) && k.Key != op ? "0" : k.Value);
                });
                ini.Set($"{Profile}/{section}", "Tuning", string.Join("/", tune));
            }
        }
    }
}
