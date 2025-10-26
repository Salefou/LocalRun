namespace LocalRun.Core.Processing;

using LocalRun.Core.Model;
using System.Threading.Tasks;

public interface IRunProcessor
{
    Task RunAsync(LocalRunConfig config);
}
