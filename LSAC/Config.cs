namespace LSAC;

using System.Text.Json;
using System.Text.Json.Serialization;

public class Config
{
    public static string ApplicationName = "LSAC";
    public static string ConfigurationFileName = "lsac.config";
    public static ConfigModel Model { get; set; } = new();
    public static string SavePath { get; set; } = string.Empty;
    
    public static string GetDefaultSavePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folderPath = Path.Combine(appData, ApplicationName);
        return Path.Combine(folderPath, ConfigurationFileName);
    }

    public static bool LoadConfig(string? customPath = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SavePath))
            {
                SavePath = customPath ?? GetDefaultSavePath();
            }

            if (!File.Exists(SavePath))
            {
                return false;
            }

            var json = File.ReadAllText(SavePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            
            Model = JsonSerializer.Deserialize<ConfigModel>(json, options) ?? new ConfigModel();
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error loading config: {e.Message}");
            return false;
        }
    }

    public static bool SaveConfig(string? customPath = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SavePath))
            {
                SavePath = customPath ?? GetDefaultSavePath();
            }

            var folderPath = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(Model, options);
            File.WriteAllText(SavePath, json);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error saving config: {e.Message}");
            return false;
        }
    }

    public static void BackupConfig()
    {
        try
        {
            if (!File.Exists(SavePath))
                return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
            var backupPath = Path.Combine(
                Path.GetDirectoryName(SavePath) ?? "",
                $"{Path.GetFileNameWithoutExtension(SavePath)}.{timestamp}.config"
            );

            // If backup already exists today, add a counter
            int counter = 1;
            string originalBackupPath = backupPath;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(
                    Path.GetDirectoryName(SavePath) ?? "",
                    $"{Path.GetFileNameWithoutExtension(SavePath)}.{timestamp}.{counter}.config"
                );
                counter++;
            }

            File.Copy(SavePath, backupPath);
            Console.WriteLine($"Config backed up to: {backupPath}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error backing up config: {e.Message}");
        }
    }
}

public class ConfigModel
{
    [JsonPropertyName("dialog")]
    public DialogConfig? Dialog { get; set; }
}

public class DialogConfig
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "Main Menu";

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("options")]
    public List<DialogOption> Options { get; set; } = new();
}

public class DialogOption
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("options")]
    public List<DialogOption>? Options { get; set; }

    public bool IsSubmenu => Options != null && Options.Count > 0;
}
