namespace LocalRun.Core.Processing;

using LocalRun.Core.Model;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IRunProcessor
{
    /// <param name="exclude">Executables not to run, e.g. the one being debugged from an IDE.</param>
    /// <param name="only">Executables to run, the others of the config left out; all of them when null.</param>
    Task RunAsync(LocalRunConfig config, IReadOnlyCollection<string>? exclude = null, IReadOnlyCollection<string>? only = null);
}
