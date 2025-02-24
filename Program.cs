using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Telegram.Bot;
using Test4Trident_monolith.Controllers;

var builder = WebApplication.CreateBuilder(args);
using var log = new LoggerConfiguration() 
    .WriteTo.File("logs/botlog.txt",            // Logs to file
        rollingInterval: RollingInterval.Day)   // Creates a new log file daily
    .CreateLogger();

builder.Logging.ClearProviders();
Log.Logger = log;
Log.Information("Global logger is online");


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();


var botToken = "";
builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));
builder.Services.AddSingleton<UserController>(); 
builder.Services.AddSingleton<DatabaseController>(); 
var app = builder.Build();

app.UseRouting();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

using var scope = app.Services.CreateScope();
var botClient = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();

string webhookUrl = "https://5be2-90-131-39-3.ngrok-free.app/bot/update";
await botClient.SetWebhook(webhookUrl);

app.Run();
Log.Information($"App is online");