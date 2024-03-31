using System.Text;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace AprilFools24;

public class WordInfo
{
    public string Word { get; set; }
    public int Count { get; set; }
    public bool Banned { get; set; }
    public bool Muted { get; set; }
}


class Program
{

    public static IConfigurationRoot Configuration { get; set; }

    private static DiscordSocketClient _client;

    private static List<ulong> ExcludeChannels = new List<ulong>
    {
        945018442522701894, // vis
        1171071661932556298, // admission to eth
        905494593200607354, // exchange sem
        896110502353195018, // gess electives minor
        1067785361062887424, // gess electives minor  forum
        755401019508260967, // infk sem 1
        772551262033018951, // infk sem 2
        810245561705824257, // infk sem 3
        810245502317756416, // infk sem 4
        810245633922695178, // infk sem 5
        810245676213862430, // infk sem 6
        810248291182051379, // master
        1155587263686774884, // master majors
        1155587489906573362, // master interfocus
        1155587423770787840, // master specialized
        770988069591449660 // studying at eth
    };

    private static List<ulong> AdminIds = new List<ulong>
    {
        153929916977643521, // battlerush
        205704051856244736, // mark (admin)
        299478604809764876, // aaron ctf
        190550937264324608, // surus
        133638242292596736, // amos
        372085649030905876, // jan
        223932775474921472, // lukas
        184682395553759233, // lu
        321022340412735509, // marc (mod)
        831172203877105674, // tetato
        473246036429897728, // yael
        123841216662994944 // elthision
    };

    public static List<ulong> BannedUsers = new List<ulong>();
    public static List<string> WordsAlpha = new List<string>();

    public static ulong GuildId = 747752542741725244;
    public static int AutoModTime = 5 * 60; // in seconds
    public static int SaveToDiskTime = 60;
    public static ulong ChannelToPostAutoMod = 1224111058793529504;
    public static ulong MessageChannelId = 1224111058793529504;
    public static double WordWithMuteProbability = 0.25;
    public static List<WordInfo> wordUsedCount = new List<WordInfo>();

    public static async Task Main(string[] args)
    {

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        Configuration = builder.Build();


        // if wordCount.json exists read the file and load the dictionary
        if (File.Exists("wordCount.json"))
        {
            string json = File.ReadAllText("wordCount.json");
            wordUsedCount = JsonConvert.DeserializeObject<List<WordInfo>>(json);
        }

        if (File.Exists("bannedUsers.json"))
        {
            string json = File.ReadAllText("bannedUsers.json");
            BannedUsers = JsonConvert.DeserializeObject<List<ulong>>(json);
        }

        // load words_alpha.txt to a list of string which are seperated by new line
        if (File.Exists("words_alpha.txt"))
        {
            string[] words = File.ReadAllLines("words_alpha.txt");
            WordsAlpha = words.ToList();
        }

        Console.WriteLine("Loaded " + wordUsedCount.Count + " words");
        Console.WriteLine("Loaded " + BannedUsers.Count + " banned users");
        Console.WriteLine("Loaded " + WordsAlpha.Count + " words_alpha");


        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };


        _client = new DiscordSocketClient(config);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync;

        // read DiscordToken from appsettings.json
        string discordToken = Configuration["DiscordToken"];

        Console.WriteLine("DiscordToken: " + discordToken);

        await _client.LoginAsync(TokenType.Bot, discordToken);
        await _client.StartAsync();

        await Task.Delay(Timeout.Infinite);
    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private static Task ReadyAsync()
    {
        Console.WriteLine($"{_client.CurrentUser} is connected!");
        return Task.CompletedTask;
    }

    private static DateTime lastWriteToDisk = DateTime.Now;

    private static DateTime lastAutoMod = DateTime.Now;

    private static async Task<bool> ProcessMessage(SocketMessage message)
    {

        // check if user is an admin
        if (AdminIds.Contains(message.Author.Id))
        {
            if (message.Content.ToLower() == "!clear")
            {
                // delete any non banned words
                wordUsedCount = wordUsedCount.Where(x => x.Banned).ToList();

                // write to disk
                string json = JsonConvert.SerializeObject(wordUsedCount, Formatting.Indented);
                File.WriteAllText("wordCount.json", json);

                await message.Channel.SendMessageAsync("Cleared the word list");

                return true;
            }

            if (message.Content.ToLower().StartsWith("!ban"))
            {
                // get the message content
                string[] messageContent = message.Content.Split(" ");
                string word = messageContent[1];

                if (ulong.TryParse(messageContent[2], out ulong userId))
                {
                    BannedUsers.Add(userId);
                    await message.Channel.SendMessageAsync("Banned user " + userId);
                }
                else
                {
                    await message.Channel.SendMessageAsync("Could not parse user id");
                }

                // save banned users to json
                string json = JsonConvert.SerializeObject(BannedUsers, Formatting.Indented);
                File.WriteAllText("bannedUsers.json", json);

                return true;
            }

            // help command
            if (message.Content.ToLower() == "!help")
            {
                string availableCommands = "Available commands: \n";
                availableCommands += "!say <message> - bot will say the message \n";
                availableCommands += "!autoMod - create a new auto mod rule \n";
                availableCommands += "!wordCount - get the top 10 words used banned and unbanned \n";
                availableCommands += "!list - get the word list as a json file \n";
                await message.Channel.SendMessageAsync(availableCommands);

                return true;
            }

            // say command 
            if (message.Content.ToLower().StartsWith("!say"))
            {
                // get the message content
                string[] messageContent = message.Content.Split(" ");
                string response = "";
                for (int i = 1; i < messageContent.Length; i++)
                {
                    response += messageContent[i] + " ";
                }
                await message.Channel.SendMessageAsync(response);

                return true;
            }

            if (message.Content.ToLower() == "!wordcount")
            {
                // send current word count sorted by count
                var topNonBanndedWords = wordUsedCount.Where(x => !x.Banned).OrderByDescending(x => x.Count).Take(10).ToList();
                var topWords = topNonBanndedWords.Select(x => x.Word + " - " + x.Count).ToList();

                string response = "Top 10 words: \n";
                response += string.Join("\n", topWords);

                await message.Channel.SendMessageAsync(response);

                response = "Top Banned words: \n";
                response += string.Join("\n", topNonBanndedWords.Select(x => x.Word + " - " + x.Count).ToList());
                await message.Channel.SendMessageAsync(response);

                return true;
            }

            if (message.Content.ToLower() == "!list")
            {
                // return word list as json file
                string json = JsonConvert.SerializeObject(wordUsedCount, Formatting.Indented);
                await message.Channel.SendFileAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), "wordCount.json");

                return true;
            }

            // create auto mod rule blocking 
            if (message.Content.ToLower() == "!automod")
            {
                try{
                    // create automod rule
                    ulong guildId = GuildId;
                    var guild = _client.GetGuild(guildId);

                    await message.Channel.SendMessageAsync("Creating new auto mod rule for guild " + guild.Name);


                    var channel = await guild.CreateAutoModRuleAsync(x =>
                    {
                        x.Name = "Test Filter";
                        x.TriggerType = AutoModTriggerType.Keyword;
                        x.KeywordFilter = new string[] { "cat", "dog", "foo", "bar" };
                        x.Actions = new AutoModRuleActionProperties[]{
                        new AutoModRuleActionProperties { Type = AutoModActionType.BlockMessage, CustomMessage = "You cannot use these words" },
                        new AutoModRuleActionProperties { Type = AutoModActionType.SendAlertMessage, ChannelId = ChannelToPostAutoMod }
                        };
                    });

                    await message.Channel.SendMessageAsync("Created new auto mod rule " + channel.Name);
                }
                catch(Exception e)
                {
                    await message.Channel.SendMessageAsync(e.Message);
                }
            }
        }


        // split message by spaces and new lines
        string[] words = message.Content.Split(new char[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];

            // word tuning
            word = word.ToLower();

            if (i == 0 && !char.IsLetter(word[0]))
                word = word.Substring(1);

            if (word.Length < 2 || word.Length > 15)
                continue;

            // remove commas and dots
            word = word.Replace(",", "");
            word = word.Replace(".", "");

            int weight = 1;

            ulong categoryId = 0;

            if (message.Channel is SocketTextChannel channel)
                categoryId = channel.Category.Id;


            if (BannedUsers.Contains(message.Author.Id))
            {
                weight = 0;
            }
            else
            {

                if (ExcludeChannels.Contains(message.Channel.Id) || ExcludeChannels.Contains(categoryId))
                {
                    weight = 5;

                    // if this word contains a character that isnt a letter or number then use a weight of 1
                    if (word.Any(x => !char.IsLetterOrDigit(x)))
                        weight = 1;

                    // if this word exists in WordsAlpha list then use weight of 50
                    if (WordsAlpha.Contains(word))
                        weight = 50;
                }
            }

            // check if the word is in the dictionary
            if (wordUsedCount.Any(x => x.Word == word))
                wordUsedCount.First(x => x.Word == word).Count += weight;
            else
                wordUsedCount.Add(new WordInfo { Word = word, Count = weight, Banned = false });
        }


        // if more than x min is passed write to disk the word count
        if (DateTime.Now.Subtract(lastWriteToDisk).TotalSeconds > SaveToDiskTime)
        {
            // write to disk
            string json = JsonConvert.SerializeObject(wordUsedCount, Formatting.Indented);
            File.WriteAllText("wordCount.json", json);
            lastWriteToDisk = DateTime.Now;
        }

        return false;
    }


    private static async Task MessageReceivedAsync(SocketMessage message)
    {
        try
        {
            // Ignore all bot messages.
            if (!message.Author.IsBot)
            {
                var cancel = await ProcessMessage(message);
                if (cancel)
                    return;
            }

            // if last automod rule is more than 5 min ago create a new automod rule

            if (DateTime.Now.Subtract(lastAutoMod).TotalSeconds > AutoModTime)
            {
                // create auto mod rule blocking 
                // create automod rule
                ulong guildId = GuildId;
                var guild = _client.GetGuild(guildId);


                double randomValue = new Random().NextDouble();

                // WE RUN EVERY 5 mins
                // For block list we got 2000 word capacity
                // For mute list we got 1000 word capacity

                // This gives us 24h * 12 = 288 times a ban is ran at best -> some downtime in the night


                if (randomValue < WordWithMuteProbability)
                {
                    // choose the top 15 words to ban
                    var bannedWords = wordUsedCount.Where(x => !x.Banned).OrderByDescending(x => x.Count).Take(15).ToList();

                    // send in channel
                    string response = "Top words im about to ban (with mute): \n";
                    response += string.Join("\n", bannedWords.Select(x => x.Word + " - " + x.Count).ToList());
                    await guild.GetTextChannel(ChannelToPostAutoMod).SendMessageAsync(response);

                    // mark the words as banned and muted
                    foreach (var word in bannedWords)
                    {
                        wordUsedCount.First(x => x.Word == word.Word).Banned = true;
                        wordUsedCount.First(x => x.Word == word.Word).Muted = true;
                    }

                    var wordsToMute = wordUsedCount.Where(x => x.Banned && x.Muted).Select(x => x.Word).ToList();

                    // make sure to limit the words to 1000 
                    if (wordsToMute.Count > 1000)
                        wordsToMute = wordsToMute.Take(1000).ToList();

                    // if rule "APRIL FOOLS MUTE" exists delete it
                    var existingRule = guild.GetAutoModRulesAsync().Result.FirstOrDefault(x => x.Name == "APRIL FOOLS MUTED WORDS");
                    if (existingRule != null)
                    {
                        Console.WriteLine("Deleting rule APRIL FOOLS MUTED WORDS");
                        await existingRule.DeleteAsync();
                    }

                    Console.WriteLine("Creating rule APRIL FOOLS MUTED WORDS with words " + wordsToMute.Count);

                    var mutedRule = await guild.CreateAutoModRuleAsync(x =>
                    {
                        x.Name = "APRIL FOOLS MUTED WORDS";
                        x.TriggerType = AutoModTriggerType.Keyword;
                        x.KeywordFilter = wordsToMute.Select(i => i.Length < 4 ? $"* {i} *" : $"*{i}*").ToArray();
                        x.Actions = new AutoModRuleActionProperties[]{
                            new AutoModRuleActionProperties { Type = AutoModActionType.BlockMessage, CustomMessage = "You naughty you! Think twice next time!" },
                            new AutoModRuleActionProperties { Type = AutoModActionType.SendAlertMessage, ChannelId = ChannelToPostAutoMod },
                            new AutoModRuleActionProperties { Type = AutoModActionType.Timeout, TimeoutDuration = TimeSpan.FromSeconds(10)}
                        };
                        x.ExemptChannels = ExcludeChannels.ToArray();
                        x.Enabled = true;
                    });

                    Console.WriteLine(mutedRule.Name + " created");

                    await guild.GetTextChannel(ChannelToPostAutoMod).SendMessageAsync("Applied new mute rule");
                }
                else
                {
                    // choose the top 15 words to ban
                    var wordsToBann = wordUsedCount.Where(x => !x.Banned).OrderByDescending(x => x.Count).Take(10).ToList();

                    // send in channel
                    string response = "Top words im about to ban: \n";
                    response += string.Join("\n", wordsToBann.Select(x => x.Word + " - " + x.Count).ToList());
                    await guild.GetTextChannel(ChannelToPostAutoMod).SendMessageAsync(response);

                    // mark the words as banned and muted
                    foreach (var word in wordsToBann)
                    {
                        wordUsedCount.First(x => x.Word == word.Word).Banned = true;
                        wordUsedCount.First(x => x.Word == word.Word).Muted = false;
                    }

                    var wordsToBlock = wordUsedCount.Where(x => x.Banned && !x.Muted).ToList();


                    if (wordsToBlock.Count <= 1100)
                    {
                        // if rule "APRIL FOOLS MUTE" exists delete it
                        var existingRule1 = guild.GetAutoModRulesAsync().Result.FirstOrDefault(x => x.Name == "APRIL FOOLS BANNED WORDS 1");
                        if (existingRule1 != null)
                        {
                            Console.WriteLine("Deleting rule APRIL FOOLS BANNED WORDS 1");
                            await existingRule1.DeleteAsync();
                        }
                    }

                    if (wordsToBlock.Count > 1000)
                    {
                        // if rule "APRIL FOOLS MUTE" exists delete it
                        var existingRule2 = guild.GetAutoModRulesAsync().Result.FirstOrDefault(x => x.Name == "APRIL FOOLS BANNED WORDS 2");
                        if (existingRule2 != null)
                        {
                            Console.WriteLine("Deleting rule APRIL FOOLS BANNED WORDS 2");
                            await existingRule2.DeleteAsync();
                        }
                    }

                    if (wordsToBlock.Count <= 1100)
                    {
                        var first1000Words = wordsToBlock.Take(1000).Select(x => x.Word).ToList();

                        Console.WriteLine("Creating rule APRIL FOOLS BANNED WORDS 1 with words " + first1000Words.Count);

                        var blockRule1 = await guild.CreateAutoModRuleAsync(x =>
                        {
                            x.Name = "APRIL FOOLS BANNED WORDS 1";
                            x.TriggerType = AutoModTriggerType.Keyword;
                            x.KeywordFilter = first1000Words.Select(i => i.Length < 4 ? $"* {i} *" : $"*{i}*").ToArray();
                            x.Actions = new AutoModRuleActionProperties[]{
                                new AutoModRuleActionProperties { Type = AutoModActionType.BlockMessage, CustomMessage = "Pretending I didn't see that! Next time, no mercy!" },
                                new AutoModRuleActionProperties { Type = AutoModActionType.SendAlertMessage, ChannelId = ChannelToPostAutoMod }
                            };
                            x.ExemptChannels = ExcludeChannels.ToArray();
                            x.Enabled = true;
                        });

                        
                        Console.WriteLine(blockRule1.Name + " created");
                    }

                    // if there are more than 1000 rules create WORD 2
                    if (wordsToBlock.Count > 1000)
                    {
                        var second1000Words = wordsToBlock.Skip(1000).Take(1000).Select(x => x.Word).ToList();

                        var blockRule2 = await guild.CreateAutoModRuleAsync(x =>
                        {
                            x.Name = "APRIL FOOLS BANNED WORDS 2";
                            x.TriggerType = AutoModTriggerType.Keyword;
                            x.KeywordFilter = second1000Words.Select(i => i.Length < 4 ? $"* {i} *" : $"*{i}*").ToArray();
                            x.Actions = new AutoModRuleActionProperties[]{
                                new AutoModRuleActionProperties { Type = AutoModActionType.BlockMessage, CustomMessage = "Pretending I didn't see that! Next time, no mercy!" },
                                new AutoModRuleActionProperties { Type = AutoModActionType.SendAlertMessage, ChannelId = ChannelToPostAutoMod }
                            };
                            x.ExemptChannels = ExcludeChannels.ToArray();
                            x.Enabled = true;
                        });

                        
                        Console.WriteLine(blockRule2.Name + " created");
                    }
                }


                // write to disk
                string json = JsonConvert.SerializeObject(wordUsedCount, Formatting.Indented);
                File.WriteAllText("wordCount.json", json);

                lastAutoMod = DateTime.Now;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);

            // send exception to channel
            await _client.GetGuild(GuildId).GetTextChannel(ChannelToPostAutoMod).SendMessageAsync(e.Message);
        }
    }
}