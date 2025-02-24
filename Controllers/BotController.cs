using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Renci.SshNet;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using TestBot4Trident.Models;

namespace Test4Trident_monolith.Controllers;

[ApiController]
[Route("bot")]
public class BotController : ControllerBase
{
    public ITelegramBotClient BotClient;
    public UserController User;
    public DatabaseController Database;

    public BotController(ITelegramBotClient botClient, UserController user, DatabaseController database)
    {
        BotClient = botClient;
        User = user;
        Database = database;
    }

    [HttpPost("update")]
    public async Task
        UpdateHandler([FromBody] UpdateDto update,
            CancellationToken token) //Telegram.Types.Update didn't work so I had to make my own dto from response
    {
        var messageText = update.message.text;
        var chatId = update.message.chat.id;
        Log.Information($"Message \"{messageText}\" sent in {chatId}");
        Console.WriteLine("Received update: " + JsonSerializer.Serialize(update));
        if (update?.message.text != null)
        {
            if (messageText == "/start")
            {
                await BotClient.SendMessage(chatId, "Welcome to the app");
                User.UserDto = new UserDto();
            }
            else
                switch (messageText) //switch for commands
                {
                    //user actions
                    case "/login":
                        await BotClient.SendMessage(chatId,
                            "Please enter your credentials in username/password format...");
                        await Database.AddOrUpdateUserState(chatId, "awaiting_login");
                        break;
                    case "/script":
                        if (User.IsLoggedIn)
                        {
                            await BotClient.SendMessage(chatId,
                                "Please enter your app name and app bundle in appname/appbundle format...");
                            await Database.AddOrUpdateUserState(chatId, "awaiting_data");
                        }
                        else
                            await BotClient.SendMessage(chatId,
                                "Sorry, you are not logged in to perform this action. Please use /login to login into your account.");

                        break;
                    case "/send":
                        if (User.IsLoggedIn)
                        {
                            await BotClient.SendMessage(chatId,
                                "Please enter your SFTP host, login and password in host|login|password format (in captions) and send a script you want to upload...");
                            await Database.AddOrUpdateUserState(chatId, "awaiting_sftp");
                        }
                        else
                            await BotClient.SendMessage(chatId,
                                "Sorry, you are not logged in to perform this action. Please use /login to login into your account.");

                        break;

                    //admin actions
                    case "/roles":
                        if (User.IsLoggedIn && User.UserDto.IsAdmin)
                        {
                            await BotClient.SendMessage(chatId,
                                "Please enter username of a user whose role you want to change and a role (admin or user) you want to assign in username/role format...");
                            await Database.AddOrUpdateUserState(chatId, "awaiting_rolechange");
                        }
                        else
                            await BotClient.SendMessage(chatId,
                                "Sorry, you are not logged in or don't have an admin role to perform this action. Please use /start to log out and /login to login into your account.");

                        break;
                    case "/uploads":
                        if (User.IsLoggedIn && User.UserDto.IsAdmin)
                        {
                            await SendLastUploads(chatId);
                        }
                        else
                            await BotClient.SendMessage(chatId,
                                "Sorry, you are not logged in or don't have an admin role to perform this action. Please use /start to log out and /login to login into an account.");

                        break;

                    //START OF THE SECTION WHERE ALL THE STRINGS THAT ARE NOT COMMANDS ARE HANDLED
                    default:
                        var userState = await Database.GetUserState(chatId);
                        if (!string.IsNullOrEmpty(userState))
                        {
                            switch (userState)
                            {
                                case "awaiting_login":
                                    if (messageText != "/login")
                                        await TryUserLogin(messageText,
                                            chatId); //if exists to prevent double requests from messing with logic
                                    break;
                                case "awaiting_data":
                                    if (messageText != "/script") await SendPHPScript(messageText, chatId);
                                    break;
                                case "awaiting_sftp":
                                    if (messageText != "/send") await SendLastScriptViaSftp(messageText, chatId);
                                    break;
                                case "awaiting_rolechange":
                                    if (messageText != "/roles") await UpdateUserRoles(messageText, chatId);
                                    break;
                            }

                            await Database.AddOrUpdateUserState(chatId, "");
                        }

                        break;
                }

            if (Database.GetUserState(chatId).Result != null)
            {
                Console.WriteLine($"New state: {Database.GetUserState(chatId).Result}");
            }

            Ok();
        }
    }

    private async Task
        TryUserLogin(string messageText,
            int chatId) //is called on /login, tries to log in user, if no user matches credentials - drops state
    {
        if (messageText.Split('/').Length == 2)
        {
            User.UserDto = await CheckUser(messageText); //if no user exists - set user to to empty and try again
            if (!string.IsNullOrEmpty(User.UserDto.Username) || !string.IsNullOrEmpty(User.UserDto.Password))
            {
                await Database.AddOrUpdateUserState(chatId, "");
                await BotClient.SendMessage(chatId, "Thank you for logging in, please choose your next action");
                Log.Information($"User:{User.UserDto.Username} Id: {User.UserDto.Id} is logged in");
            }
            else
            {
                await Database.AddOrUpdateUserState(chatId, "");
                await BotClient.SendMessage(chatId, "There is no user with those credentials, please try again");
                Log.Information($"Failed login attempt, credentials provided: {messageText}");
            }
        }
        else if (string.IsNullOrEmpty(User.UserDto.Username) || string.IsNullOrEmpty(User.UserDto.Password))
        {
            await BotClient.SendMessage(chatId, "There is no user with those credentials, please try again");
        }
    }

    public async Task<UserDto> CheckUser(string loginpassword) //returns user if found, empty user if not
    {
        string username = loginpassword.Split('/')[0];
        string password = loginpassword.Split('/')[1];
        User.UserDto = new UserDto() {Username = username, Password = password};
        var users = await Database.GetUsers();
        foreach (var user in users)
        {
            if (user.Username == username && user.Password == password)
            {
                return user;
            }
        }

        return new UserDto {Id = 0, Username = "", Password = "", IsAdmin = false};
    }

    public async Task
        SendPHPScript(string messageText, int chatId) //reads script from file and sends along with secrets to user
    {
        if (messageText.Split('/').Length == 2)
        {
            string secrets = await User.WriteScript(messageText);

            // Read the script content BEFORE opening the file for sending
            string scriptContent = await System.IO.File.ReadAllTextAsync("script.php");
            using (var fileStream = new FileStream("script.php", FileMode.Open, FileAccess.Read))
            {
                var inputFile = new InputFileStream(fileStream, "script.php");

                // Send the document
                await BotClient.SendDocument(chatId, inputFile, $"These are your secrets:\n{secrets}");
            }

            // Save to database AFTER sending
            await Database.SaveScript(User.UserDto.Id, scriptContent);
            Log.Information($"Script from {User.UserDto.Id} is saved");
        }
    }

    public async Task SendLastScriptViaSftp(string messageText, int chatId)
    {
        string sftpHost = messageText.Split('|')[0];
        string sftpUsername = messageText.Split('|')[1];
        string sftpPassword = messageText.Split('|')[2];
        string remotePath = "/SFTPUploads/"; //replace with remote path
        string filename = "upload_script.php";
        try
        {
            using MemoryStream stream =
                new MemoryStream(Encoding.UTF8.GetBytes(Database.GetLastScript(User.UserDto.Id).Result));
            using (FileStream fileStream = new FileStream(filename, FileMode.OpenOrCreate, FileAccess.Write))
            {
                await stream.CopyToAsync(fileStream);
            }

            using (var sftpClient = new SftpClient(sftpHost, sftpUsername, sftpPassword))
            {
                sftpClient.Connect();
                using (var fileStream = new FileStream(filename, FileMode.Open))
                {
                    sftpClient.UploadFile(fileStream, remotePath + "script.php");
                }

                // Disconnect after the upload
                sftpClient.Disconnect();
                await Database.AddOrUpdateUserState(chatId, "");
                await BotClient.SendMessage(chatId,
                    $"Your file was successfully uploaded to remote location {remotePath}, thank you for using this bot");
                Log.Information(
                    $"User:{User.UserDto.Username} Id: {User.UserDto.Id} sent a script via sftp to {messageText}");
            }
        }
        catch (Exception ex)
        {
            await Database.AddOrUpdateUserState(chatId, "");
            await BotClient.SendMessage(chatId, $"Error during SFTP upload:{ex.Message}");
            Log.Error($"User:{User.UserDto.Username} Id: {User.UserDto.Id} failed to send script to {messageText}");
        }
    }

    public async Task UpdateUserRoles(string messageText, int chatId)
    {
        bool admin = false;
        string role = messageText.Split('/')[1];
        string username = messageText.Split('/')[0];
        var userExists = Database.GetUsers().Result.FirstOrDefault(x => x.Username == messageText.Split('/')[0]) !=
                         null;
        if (userExists)
        {
            if (role == "admin")
            {
                admin = true;
                await Database.UpdateUserRoles(username, admin);
            }
            else if (role == "user")
            {
                await Database.UpdateUserRoles(username, admin);
            }

            await BotClient.SendMessage(chatId, $"{username}'s role updated to: {role}");
            Log.Information(
                $"User:{User.UserDto.Username} Id: {User.UserDto.Id} has updated {username} role to {role}");
        }
        else
        {
            await BotClient.SendMessage(chatId,
                $"There is no user with provided username ({username}), please try again");
            Log.Error(
                $"User:{User.UserDto.Username} Id: {User.UserDto.Id} failed to update role {role} for {username}");
        }
    }

    public async Task SendLastUploads(int chatId)
    {
        var uploads = await Database.GetLastUploads();
        foreach (var upload in uploads)
        {
            await BotClient.SendMessage(chatId,
                $"Uploaded by userId: {upload.UserId}\nDate of upload: {upload.UploadedAt.ToString()}\nPHP script uploaded : {upload.Script}");
        }

        Log.Information($"User:{User.UserDto.Username} Id: {User.UserDto.Id} checked last uploads");
    }
}