using System;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>Command line: <c>--patient &lt;id&gt; --course &lt;id&gt; --plan &lt;id&gt; [--fake]</c> (all optional).</summary>
    public sealed class LaunchOptions
    {
        public string PatientId { get; private set; } = "";
        public string CourseId { get; private set; } = "";
        public string PlanId { get; private set; } = "";
        public bool Fake { get; private set; }

        public static LaunchOptions Parse(string[] args)
        {
            var o = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string Next() => i + 1 < args.Length ? args[++i] : "";
                switch (a)
                {
                    case "--patient": o.PatientId = Next(); break;
                    case "--course": o.CourseId = Next(); break;
                    case "--plan": o.PlanId = Next(); break;
                    case "--fake": o.Fake = true; break;
                }
            }
            return o;
        }

        public override string ToString() =>
            $"patient='{PatientId}' course='{CourseId}' plan='{PlanId}' fake={Fake}";
    }
}
