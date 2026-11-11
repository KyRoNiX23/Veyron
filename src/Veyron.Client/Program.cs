
using Veyron.Network;
using Veyron.Network.Providers;

namespace Veyron.Client;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var providerFactory = new ProviderFactory();
        var connectionManager = new ConnectionManager(providerFactory);

        Application.Run(new Forms.MainForm(connectionManager));
    }
}