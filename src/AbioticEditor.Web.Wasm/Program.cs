using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AbioticEditor.Web.Wasm;
using AbioticEditor.Web.Wasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<PlayerSaveSession>();
builder.Services.AddScoped<BrowserFilePickerService>();
// The seam the shared editor screens reach save files through. On this host it is the browser's
// File System Access API rather than a disk.
builder.Services.AddScoped<BrowserSaveFileSystem>();
builder.Services.AddScoped<AbioticEditor.Web.Services.ISaveFileSystem>(sp => sp.GetRequiredService<BrowserSaveFileSystem>());
builder.Services.AddScoped<AbioticEditor.Ui.IFilePicker>(sp => sp.GetRequiredService<BrowserFilePickerService>());
builder.Services.AddScoped<AbioticEditor.Ui.IFolderPicker>(sp => sp.GetRequiredService<BrowserFilePickerService>());

await builder.Build().RunAsync();
