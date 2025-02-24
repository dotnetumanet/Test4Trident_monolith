using System.Security.Cryptography;
using System.Text;

namespace Test4Trident_monolith.Controllers;

public class UserController
{
    public UserDto UserDto;

    public bool IsLoggedIn
    {
        get
        {
            if (UserDto.Username != "" && UserDto.Password != "" && UserDto.Id != 0)
            {
                return true;
            }

            return false;
        }
    }

    public UserController()
    {
        UserDto = new UserDto();
    }

    public async Task<string> WriteScript(string messageText) //writes script to script.php file
    {
        string appname = messageText.Split('/')[0];
        string appbundle = messageText.Split('/')[1];
        string filepath = "script.php";
        string secretkey = GenerateSecretKey();
        string secretkeyparams = GenerateSecretKeyParameters();
        var phpscript = await GenerateScript(appname, appbundle, secretkey, secretkeyparams);
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(phpscript));
        using FileStream fileStream = new FileStream(filepath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream);
        Console.WriteLine($"File saved as {filepath}");

        // Reset stream position after saving
        stream.Position = 0;
        return $"Secret key: {secretkey}\nSecret key parameters: {secretkeyparams}";
    }

    public async Task<string>
        GenerateScript(string appname, string appbundle, string secretkey,
            string secretkeyparams) //isn't sure if secret key params should be the same as secret key?
    {
        string script =
            $"<?php\n$appName = '{appname}';\n$appBundle = '{appbundle}';\n$secretKey = '{GenerateSecretKey()}';\nif($secretKey == $_GET['{GenerateSecretKeyParameters()}']){{\n    echo 'Привет я приложение '. $appName .' моя ссылка на гугл плей https://play.google.com/store/apps/details?id='. $appBundle ;\n}}";
        return script;
    }

    private string GenerateSecretKey(int keySize = 8)
    {
        byte[] keyBytes = new byte[keySize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(keyBytes);
        }

        return Convert.ToBase64String(keyBytes);
    }

    private string GenerateSecretKeyParameters(int keySize = 8)
    {
        byte[] keyBytes = new byte[keySize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(keyBytes);
        }

        return Convert.ToBase64String(keyBytes);
    }
}