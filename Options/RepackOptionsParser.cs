namespace LocalRun.Options;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal class RepackOptionsParser
{
    public static RepackOptions Parse(string[] args)
    {
        string? suffix = null;
        bool patchBump = false;
        bool remoteAsRef = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--alpha":
                    suffix = "alpha";
                    patchBump = true;
                    break;
                case "--remote":
                    suffix = "zlocal";
                    patchBump = true;
                    remoteAsRef = true;
                    break;
            }
        }

        return new RepackOptions()
        {
            BumpOptions = new BumpOptions()
            {
                Suffix = suffix,
                PatchBump = patchBump,
            },
            UseRemoteAsVersionReference = remoteAsRef,
        };
    }
}

