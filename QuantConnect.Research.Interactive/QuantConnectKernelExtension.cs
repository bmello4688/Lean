using Deedle;
using Microsoft.AspNetCore.Html;

using System;
using System.Collections.Generic;
using System.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Formatting;

using static Microsoft.DotNet.Interactive.Formatting.PocketViewTags;
using System.IO;
using System.Reflection;

namespace QuantConnect.Research.Interactive
{
    public class QuantConnectKernelExtension : IKernelExtension
    {
        private const string rootAssemblyDirectory = "/Lean/Launcher/bin/Debug";

        public async Task OnLoadAsync(Kernel kernel)
        {
            RegisterDeedleFrame();
            PocketView view = div(
                    code(nameof(QuantConnectKernelExtension)),
                    " is loaded. It adds visualizations for Deedle",
                    code(typeof(Frame<DateTime, string>)),
                    ". Try it out"
                );
            Display(view);

            Initialize(kernel);

            await LoadQuantConnect(kernel);

            PocketView view1 = div(
                    code(nameof(QuantConnectKernelExtension)),
                    " is loaded. It adds usage to QuanConnect libraries",
                    ". Try it out"
                );
            Display(view1);
        }

        private async Task SignInToBrokerages(Kernel kernel)
        {
            await kernel.SubmitCodeAsync(@"using System;");
            await kernel.SubmitCodeAsync(@"using TDAmeritradeApi.Client;");

            await kernel.SubmitCodeAsync("public class TDJupyterCredentialProvider : ICredentials { public string GetPassword() { return password(\"Password: \").GetClearTextPassword(); }  public string GetSmsCode() { return input(\"Sms: \"); }  public string GetUserName() { return input(\"Username: \"); } }");
            await kernel.SubmitCodeAsync(@"var tdCredentials = new TDJupyterCredentialProvider();");

            await kernel.SubmitCodeAsync(@"using QuantConnect;");
            await kernel.SubmitCodeAsync(@"using QuantConnect.Configuration;");
            await kernel.SubmitCodeAsync("var clientId = Config.Get(\"td-client-id\", \"\");");
            await kernel.SubmitCodeAsync("var redirectUri = Config.Get(\"td-redirect-uri\", \"\");");
            await kernel.SubmitCodeAsync(@"var tdClient = new TDAmeritradeClient(clientId, redirectUri);");
            await kernel.SubmitCodeAsync(@"tdClient.LogIn(tdCredentials).Wait();");

            await Task.CompletedTask;
        }

        private static void Display(PocketView view)
        {
            if (KernelInvocationContext.Current is { } context)
            {
                context.Display(view);
            }
        }

        private void Initialize(Kernel kernel)
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            //var parentDirectory = Directory.GetParent(currentDirectory).FullName;
            var parentDirectory = rootAssemblyDirectory;

            // If our parent directory contains QC Dlls use it, otherwise default to current working directory
            // In cloud and CLI research cases we expect the parent directory to contain the Dlls; but locally it may be cwd
            var directoryToLoad = Directory.GetFiles(parentDirectory, "QuantConnect.*.dll").Any() ? parentDirectory : currentDirectory;

            // Load in all dll's from this directory
            Console.WriteLine($"Initialize: Loading assemblies from {directoryToLoad}");
            foreach (var file in Directory.GetFiles(directoryToLoad, "*.dll"))
            {
                try
                {
                    Assembly.LoadFrom(file.ToString());
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

        }

        private async Task LoadQuantConnect(Kernel kernel)
        {
            await kernel.SubmitCodeAsync($"#load \"{rootAssemblyDirectory}/QuantConnect.csx\"");

            await Task.CompletedTask;
        }

        private static void RegisterDeedleFrame()
        {
            Formatter.Register<Frame<DateTime, string>>((df, writer) =>
            {
                var headers = new List<IHtmlContent>();
                headers.Add(th(i("DateTime(Index)")));
                headers.AddRange(df.Columns.Observations.Select(kvp => (IHtmlContent)th(kvp.Key)));
                var rows = new List<List<IHtmlContent>>();

                for (var i = 0; i < df.RowCount; i++)
                {
                    var cells = new List<IHtmlContent>();
                    cells.Add(td(df.RowIndex.Keys[i]));
                    foreach (var obj in df.Rows.GetAt(i).Values)
                    {
                        cells.Add(td(obj));
                    }
                    rows.Add(cells);
                }

                var t = table(
                    thead(
                        headers),
                    tbody(
                        rows.Select(
                            r => tr(r))));

                writer.Write(t);
            }, "text/html");
        }
    }
}
