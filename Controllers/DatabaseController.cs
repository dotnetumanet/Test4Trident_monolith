using Test4Trident_monolith.Dtos;
using Microsoft.Data.SqlClient;
using Dapper;

namespace Test4Trident_monolith.Controllers;

public class DatabaseController
{
    public string connectionString { get; set; }

    public DatabaseController()
    {
        connectionString =
            "Data Source=.;Initial Catalog=Test4trident;TrustServerCertificate=True;Persist Security Info=True;User ID=sa;Password=Labas123!@#";
    }

    public async Task<List<UserDto>> GetUsers()
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        string query = "SELECT Id, Username, Password, IsAdmin FROM Users";
        using var cmd = new SqlCommand(query, connection);
        using var reader = await cmd.ExecuteReaderAsync();
        List<UserDto> users = new List<UserDto>();
        while (reader.Read())
        {
            users.Add(new UserDto
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                Password = reader.GetString(2),
                IsAdmin = reader.GetBoolean(3),
            });
        }

        connection.Close();
        return users;
    }

    public async Task AddOrUpdateUserState(int userId, string state)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var existingState = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT UserState FROM UserStates WHERE UserId = @UserId", new {UserId = userId});
            if (existingState == null)
            {
                await connection.ExecuteAsync("INSERT INTO UserStates (UserId, UserState) VALUES (@UserId, @UserState)",
                    new {UserId = userId, UserState = state});
            }
            else
            {
                await connection.ExecuteAsync("UPDATE UserStates SET UserState = @UserState WHERE UserId = @UserId",
                    new {UserId = userId, UserState = state});
            }
        }
    } //used for states manipulation

    public async Task UpdateUserRoles(string username, bool isadmin)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.ExecuteAsync("UPDATE Users SET IsAdmin = @IsAdmin WHERE Username = @Username",
                new {IsAdmin = isadmin, Username = username});
        }
    }

    public async Task<string>? GetUserState(int userId)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var result = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT UserState FROM UserStates WHERE UserId = @UserId", new {UserId = userId});
            return result;
        }
    }

    public async Task<List<UploadDto>> SaveUpload(int userId, string script) //saves Uploaded scripts
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        string query = @" INSERT INTO UserUploads (UserId, Script, UploadedAt)
        VALUES (@UserId, @Script, GETDATE());";
        var result = await connection.QueryAsync<UploadDto>(query, new {UserId = userId, Script = script});
        return result.ToList();
    }

    public async Task
        SaveScript(int userId,
            string script) //saves users' last script, one script per user. could be utilized to upload last script many times, although smarter approach would be to let user send his own script to upload?
    {
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var existingState = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT LastScript FROM GeneratedScripts WHERE UserId = @UserId", new {UserId = userId});
            if (existingState == null)
            {
                await connection.ExecuteAsync(
                    "INSERT INTO GeneratedScripts (UserId, LastScript) VALUES (@UserId, @LastScript)",
                    new {UserId = userId, LastScript = script});
            }
            else
            {
                await connection.ExecuteAsync(
                    "UPDATE GeneratedScripts SET LastScript = @LastScript WHERE UserId = @UserId",
                    new {UserId = userId, LastScript = script});
            }
        }
    }

    public async Task<string> GetLastScript(int userId)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var result = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT LastScript FROM GeneratedScripts WHERE UserId = @UserId", new {UserId = userId});
        return result;
    } //gets last script by userid

    public async Task<List<UploadDto>> GetLastUploads() //gets last 10 uploads by time
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var result = await connection.QueryFirstOrDefaultAsync<List<UploadDto>>(
            "SELECT TOP 10 UserId, Script, UploadedAt FROM LastUploadedScripts");
        return result;
    }
}