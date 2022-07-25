using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NBitcoin;
using OutpointProximity;
using OutpointProximity.UI;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(_ => new UTXORepo(Network.Main, ScriptPubKeyType.Segwit));

await builder.Build().RunAsync();