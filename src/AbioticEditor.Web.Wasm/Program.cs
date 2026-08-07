using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AbioticEditor.Web.Wasm;
using AbioticEditor.Web.Wasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<PlayerSaveSession>();
builder.Services.AddScoped<BrowserFilePickerService>();
builder.Services.AddScoped<AbioticEditor.Ui.IFilePicker>(sp => sp.GetRequiredService<BrowserFilePickerService>());
builder.Services.AddScoped<AbioticEditor.Ui.IFolderPicker>(sp => sp.GetRequiredService<BrowserFilePickerService>());

await builder.Build().RunAsync();
