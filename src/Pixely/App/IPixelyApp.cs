using System.Diagnostics.CodeAnalysis;
using Pixely.DependencyInjection;

namespace Pixely.App;

public interface IPixelyApp : IDisposable
{
    ServiceProvider ServiceProvider { get; }
    T GetRequiredService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>() where T : class;
    int Run();
    bool RunFrame();
}
