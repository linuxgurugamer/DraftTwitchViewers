using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DraftTwitchViewers
{
    /// <summary>
    /// A KSP Scenario module which handles all draft functions, both internal and external.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, new GameScenes[] { GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.TRACKSTATION })]
    class ScenarioDraftManager : ScenarioModule
    {
        #region Instance

        /// <summary>
        /// The public instance of this class.
        /// </summary>
        public static ScenarioDraftManager Instance { get; private set; }

        #endregion

        #region Settings Strings

        /// <summary>
        /// The settings save location.
        /// </summary>
        private string settingsLocation = "GameData/DraftTwitchViewers/";

        #endregion

        #region Global Settings

        /// <summary>
        /// The twitch channel.
        /// </summary>
        public string channel = "";
        /// <summary>
        /// Add "Kerman" to every name?
        /// </summary>
        private bool addKerman = true;
        /// <summary>
        /// The list of mods in chat which shouldn't be drafted.
        /// </summary>
        public List<string> BotsToRemove;
        /// <summary>
        /// The list of users pulled for a drawing.
        /// </summary>
        public List<string> DrawnUsers;

        static public bool pausedForFailure = false;
        #endregion

        #region Per-Save (Local) Settings

        /// <summary>
        /// The list of users already drafted.
        /// </summary>
        public List<string> AlreadyDrafted;

        #endregion

        #region Misc Variables

        /// <summary>
        /// Has something changed that we need to save?
        /// </summary>
        private bool needSave = false;
        /// <summary>
        /// The current delay time for saving.
        /// </summary>
        private float currentSaveDelay = 1f;
        /// <summary>
        /// The max delay time for saving.
        /// </summary>
        private const float maxSaveDelay = 1f;
        /// <summary>
        /// The client ID string used to access the Twitch API.
        /// </summary>
        private const string clientID = "2gejhahzzkdfssseh8t64x6zkqmdvb0";

        #endregion

        #region Properties

        /// <summary>
        /// AddKerman property. Triggers autosave when changed.
        /// </summary>
        public bool AddKerman
        {
            get { return addKerman; }
            set { if (addKerman != value) { addKerman = value; SaveGlobalSettings(); } }
        }

        /// <summary>
        /// Channel property. Triggers autosave (after delay) when changed.
        /// </summary>
        public string Channel
        {
            get { return channel; }
            set { if (channel != value) { channel = value; needSave = true; currentSaveDelay = 0f; } }
        }

        #endregion

        #region Unity Functions

        /// <summary>
        /// Called when the MonoBehaviour is first created.
        /// </summary>
        public override void OnAwake()
        {
            if (!Instance)
            {
                Instance = this;

                #region Global Settings Load

                // Create an empty bot list.
                BotsToRemove = new List<string>();
                // Create an empty drawing list.
                DrawnUsers = new List<string>();

                // Load global settings.
                ConfigNode globalSettings = ConfigNode.Load(settingsLocation + "GlobalSettings.cfg");
                // If the file exists,
                if (globalSettings != null)
                {
                    // Used to save if any corrupt nodes were found.
                    bool doSave = false;

                    #region Draft Settings Load

                    // Get the DRAFT node.
                    ConfigNode draftSettings = globalSettings.GetNode("DRAFT");

                    // If the DRAFT node exists,
                    if (draftSettings != null)
                    {
                        // Get the global settings.
                        if (draftSettings.HasValue("channel")) { channel = draftSettings.GetValue("channel"); }
                        if (draftSettings.HasValue("addKerman")) { try { addKerman = bool.Parse(draftSettings.GetValue("addKerman")); } catch { } }
                    }
                    // If the DRAFT node doesn't exist,
                    else
                    {
                        // Log a warning that is wasn't found.
                        Logger.DebugWarning("GlobalSettings.cfg WAS found, but the DRAFT node was not. Using defaults.");
                    }

                    #endregion

                    #region Bot Settings Load

                    // Get the list of bots to remove from drafts and drawings.
                    ConfigNode botsSettings = globalSettings.GetNode("BOTS");

                    // If the BOTS node exists,
                    if (botsSettings != null)
                    {
                        // Get a list of BOT nodes.
                        ConfigNode[] botNodes = botsSettings.GetNodes("BOT");

                        // Iterate through,
                        foreach (ConfigNode c in botNodes)
                        {
                            // If the node has a name value,
                            if (c.HasValue("name"))
                            {
                                // Add the string name to the list of bots to remove.
                                BotsToRemove.Add(c.GetValue("name"));
                            }
                            // If the node doesn't have a name value,
                            else
                            {
                                // Log a warning that this node is corrupt and is being removed.
                                Logger.DebugWarning("Corrupt BOT node. Removing.");
                                // Remove the corrupt node.
                                botsSettings.RemoveNode(c);
                                // Set doSave to true so the corrupt nodes remain gone.
                                doSave = true;
                            }
                        }
                    }
                    // If the BOTS node doesn't exist,
                    else
                    {
                        // Log a warning that is wasn't found.
                        Logger.DebugWarning("GlobalSettings.cfg WAS found, but the BOTS node was not. Using empty list.");
                    }

                    #endregion

                    #region Drawing Settings Load

                    // Get the list of users which have already won a drawing.
                    ConfigNode drawnSettings = globalSettings.GetNode("DRAWN");

                    // If the DRAWN node exists,
                    if (drawnSettings != null)
                    {
                        // Get a list of USER nodes.
                        ConfigNode[] drawnNodes = drawnSettings.GetNodes("USER");

                        // Iterate through,
                        foreach (ConfigNode c in drawnNodes)
                        {
                            // If the node has a name value,
                            if (c.HasValue("name"))
                            {
                                // Add the string name to the list of drawn users.
                                DrawnUsers.Add(c.GetValue("name"));
                            }
                            // If the node doesn't have a name value,
                            else
                            {
                                // Log a warning that this node is corrupt and is being removed.
                                Logger.DebugWarning("Corrupt DRAWN.USER node. Removing.");
                                // Remove the corrupt node.
                                drawnSettings.RemoveNode(c);
                                // Set doSave to true so the corrupt nodes remain gone.
                                doSave = true;
                            }
                        }
                    }
                    // If the DRAWN node doesn't exist,
                    else
                    {
                        // Log a warning that is wasn't found.
                        Logger.DebugWarning("GlobalSettings.cfg WAS found, but the DRAWN node was not. Using empty list.");
                    }

                    #endregion

                    // If corrupt nodes were found and removed,
                    if (doSave)
                    {
                        // Save the settings file so they remain gone.
                        globalSettings.Save(settingsLocation + "GlobalSettings.cfg");
                    }
                }
                // If the file doesn't exist,
                else
                {
                    // Log a warning that it wasn't found.
                    Logger.DebugWarning("GlobalSettings.cfg wasn't found. Using defaults.");
                }

                #endregion

                // Initialize the regex array.
                InitRegexes();
            }
            else
            {
                Logger.DebugWarning("ScenarioDraftManager instance still exists.");
                DestroyImmediate(this);
            }
        }

        /// <summary>
        /// Called when the MonoBehaviour is updated.
        /// </summary>
        void Update()
        {
            // Update the save delay if needed.
            if (currentSaveDelay < maxSaveDelay)
            {
                currentSaveDelay += Time.deltaTime;
            }
            // Save if the delay has been reached.
            else if (needSave)
            {
                SaveGlobalSettings();
                needSave = false;
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region KSP Functions

        /// <summary>
        /// Loads the local game settings for this scenario.
        /// </summary>
        /// <param name="node">The config node for this scenario.</param>
        public override void OnLoad(ConfigNode node)
        {
            #region Local

            AlreadyDrafted = new List<string>();

            // Get the DRAFTED node.
            ConfigNode draftedNode = node.GetNode("DRAFTED");

            // If the DRAFTED node exists,
            if (draftedNode != null)
            {
                // Get a list of USER nodes.
                ConfigNode[] draftNodes = draftedNode.GetNodes("USER");

                // Iterate through,
                foreach (ConfigNode c in draftNodes)
                {
                    // If the node has a name value,
                    if (c.HasValue("name"))
                    {
                        // Add the string name to the list of users already drafted.
                        AlreadyDrafted.Add(c.GetValue("name"));
                    }
                }
            }
            // If the DRAFTED node doesn't exist,
            else
            {
                // Log a warning that is wasn't found.
                Logger.DebugWarning("DRAFTED node not found. Using empty list.");
            }

            LegacyLoadLocalSettings();

            #endregion
        }

        /// <summary>
        /// Saves the local game settings for this scenario.
        /// </summary>
        /// <param name="node">The config node for this scenario.</param>
        public override void OnSave(ConfigNode node)
        {
            SaveGlobalSettings();

            #region Local

            if (node.HasNode("DRAFTED"))
            {
                node.RemoveNode("DRAFTED");
            }

            ConfigNode draftedNode = node.AddNode(new ConfigNode("DRAFTED"));

            foreach (string name in AlreadyDrafted)
            {
                ConfigNode discoveryNode = draftedNode.AddNode(new ConfigNode("USER"));

                discoveryNode.AddValue("name", name);
            }

            #endregion
        }

        /// <summary>
        /// Loads local settings.
        /// </summary>
        public void LegacyLoadLocalSettings()
        {
            // Set the save location.
            string saveLocation = "saves/" + HighLogic.CurrentGame.Title.Substring(0, HighLogic.CurrentGame.Title.LastIndexOf(' ')) + "/";

            // Load local settings.
            ConfigNode localSettings = ConfigNode.Load(saveLocation + "DTVLocalSettings.cfg");
            // If the file exists,
            if (localSettings != null)
            {
                // Used to save if any corrupt nodes were found.
                bool doSave = false;

                #region Drafted Users Load
                // Get the DRAFTED node.
                ConfigNode draftedUsers = localSettings.GetNode("DRAFTED");

                // If the DRAFTED node exists,
                if (draftedUsers != null)
                {
                    // The legacy save was found. Load it and remove it.
                    Logger.DebugWarning("Legacy file \"DTVLocalSettings.cfg\" was found. Loading and deleting.");

                    // Get a list of USER nodes.
                    ConfigNode[] userNodes = draftedUsers.GetNodes("USER");

                    // Iterate through,
                    foreach (ConfigNode c in userNodes)
                    {
                        // If the node has a name value,
                        if (c.HasValue("name"))
                        {
                            // Add the string name to the list of users already drafted.
                            AlreadyDrafted.Add(c.GetValue("name"));
                        }
                        // If the node doesn't have a name value,
                        else
                        {
                            // Log a warning that this node is corrupt and is being removed.
                            Logger.DebugWarning("Corrupt USER node. Removing.");
                            // Remove the corrupt node.
                            draftedUsers.RemoveNode(c);
                            // Set doSave to true so the corrupt nodes remain gone.
                            doSave = true;
                        }
                    }

                    localSettings.RemoveNode(draftedUsers);
                    doSave = true;
                }
                #endregion

                // If corrupt nodes were found and removed,
                if (doSave)
                {
                    // Save the settings file so they remain gone.
                    localSettings.Save(saveLocation + "DTVLocalSettings.cfg");
                }
            }
        }

        #endregion

        #region Draft function

        static string RealUserName(string realUsername)
        {
            return realUsername + (Instance.addKerman ? " Kerman" : "");
        }

        /// <summary>
        /// Drafts a Kerbal, invoking the suplied success Action if the draft succeeds, or the failure Action if the draft fails.
        /// </summary>
        /// <param name="success">The Action to invoke on draft success. Provides a dictionary which will contain a "winner" entry for drawings, or a "name" and "job" entry for drafts.</param>
        /// <param name="failure">The Action to invoke on draft failure. Provides a string reason the draft failed.</param>
        /// <param name="forDrawing">Whether the draft is for a drawing, or for an actual draft.</param>
        /// <param name="suppressSave">If true, the drafted user will not be saved.</param>
        /// <param name="job">The job for the Kerbal. Optional and defaults to "Any" and is not needed if forDrawing is true.</param>
        /// <returns>The IEnumerator (used for making the draft asynchronously).</returns>
        public static IEnumerator DraftKerbal(Action<Dictionary<string, string>> success, Action<string> failure, bool forDrawing, bool suppressSave, string job = "Any")
        {
            if (pausedForFailure)
                yield return null;
            // If a channel hasn't been input yet,
            if (string.IsNullOrEmpty(Instance.channel))
            {
                // Invoke failure.
                failure.Invoke("Please specify a channel!");
            }
            // Else, continue.
            else
            {
                Log.Info("ScenarioDraftManager.DraftKerbal");
                // Creates a new Unity web request (WWW) using the provided channel.
                WWW getList = new WWW("http://tmi.twitch.tv/group/user/" + Instance.channel + "/chatters?client_id=" + clientID);

                // Waits for the web request to finish.
                yield return getList;

                // Check for errors.
                if (string.IsNullOrEmpty(getList.error))
                {
                    // Parse the result into a list of users, still lowercased.
                    List<string> usersInChat = new List<string>();
                    usersInChat.AddRange(Instance.ParseIntoNameArray(getList.text, "moderators"));
                    usersInChat.AddRange(Instance.ParseIntoNameArray(getList.text, "staff"));
                    usersInChat.AddRange(Instance.ParseIntoNameArray(getList.text, "admins"));
                    usersInChat.AddRange(Instance.ParseIntoNameArray(getList.text, "global_mods"));
                    usersInChat.AddRange(Instance.ParseIntoNameArray(getList.text, "viewers"));

                    foreach (var s in usersInChat)
                    {
                        Log.Info("usersInChat: " + s);
                    }
                    // Remove any bots present.
                    foreach (string bot in Instance.BotsToRemove)
                    {
                        Log.Info("Removing bot: " + bot);
                        usersInChat.Remove(bot);
                    }

                    // If it's for a drawing, remove drawn users. If it's for drafting, remove drafted users.
                    if (forDrawing)
                    {
                        // Remove any users who were already drafted.
                        foreach (string drawn in Instance.DrawnUsers)
                        {
                            usersInChat.Remove(drawn);
                        }
                    }
                    else
                    {
                        // Remove any users who were already drafted.
                        foreach (string drafted in Instance.AlreadyDrafted)
                        {
                            Log.Info("Removing drafted: " + drafted);
                            usersInChat.Remove(drafted);
                        }

                        // LGG: added check to be sure that the same user isn't a kerbal already
                        for (int i = usersInChat.Count - 1; i >= 0; i--)
                        {
                            var s = RealUserName(usersInChat[i]).ToLower();
                            
                            for (int i1 = 0; i1 < HighLogic.CurrentGame.CrewRoster.Count; i1++)
                            {
                                if (HighLogic.CurrentGame.CrewRoster[i1].name.ToLower() == s)
                                {
                                    usersInChat.Remove(usersInChat[i]);
                                    break;
                                }
                            }
                        }

                        // Create a new list which will be used to remove from the user list.
                        List<string> toRemove = new List<string>();

                        // Iterate through the regexes.
                        foreach (Regex r in Instance.regexes)
                        {
                            // Iterate through each username per regex.
                            foreach (string u in usersInChat)
                            {
                                // If the current regex matches the current username,
                                if (r.IsMatch(u))
                                {
                                    // Mark the name for removal by adding it to the removal list.
                                    toRemove.Add(u);
                                }
                            }
                        }

                        // Iterate through the removal list and remove each entry from the user list.
                        foreach (string r in toRemove)
                        {
                            usersInChat.Remove(r);
                        }

                        // If for drawing, perform drawing code.
                        if (forDrawing)
                        {
                            // If the user list is empty,
                            if (usersInChat.Count == 0)
                            {
                                // Invoke the failure Action, allowing the caller to handle this error.
                                failure.Invoke("Can't draw! No more valid users.\nEmpty the drafted user list to draft unused viewers.");
                            }
                            else
                            {
                                // Gets a random user from the list.
                                string userDrawn = usersInChat[UnityEngine.Random.Range(0, usersInChat.Count)];

                                // Creates a new Unity web request (WWW) using the user chosen.
                                Hashtable headers = new Hashtable();
                                headers.Add("Client-ID", clientID);
                                WWW getUser = new WWW("https://api.twitch.tv/helix/users?login=" + userDrawn, null, headers);

                                // Waits for the web request to finish.
                                yield return getUser;

                                // Check for errors.
                                if (string.IsNullOrEmpty(getUser.error))
                                {
                                    // Parses the real username of the chosen user.
                                    string realUsername = getUser.text.Substring(getUser.text.IndexOf("\"display_name\""));
                                    realUsername = realUsername.Substring(realUsername.IndexOf(":") + 2);
                                    realUsername = realUsername.Substring(0, realUsername.IndexOf(",") - 1);

                                    // If the save is not supressed,
                                    if (!suppressSave)
                                    {
                                        // Save the new user in the drawing file.
                                        Instance.DrawnUsers.Add(userDrawn);
                                        Instance.SaveDrawn();
                                    }

                                    Dictionary<string, string> winner = new Dictionary<string, string>();
                                    winner.Add("winner", realUsername);

                                    // Invoke the success Action, allowing the caller to continue.
                                    success.Invoke(winner);
                                }
                                // If there is an error,
                                else
                                {
                                    // Invoke failure, stating web error.
                                    failure.Invoke("Web error: " + getUser.error);
                                }
                            }
                        }
                        // Else, perform draft code.
                        else
                        {
                            // Set up variables used to exit the search loop.
                            bool foundProperKerbal = false;
                            bool failedToFindOne = false;
                            int searchAttempts = 0;

                            // Set up Kerbal data variables.
                            string oddUsername = null;
                            string realUsername = null;
                            string realJob = job;

                            // Randomize the job if none was specified.
                            if (realJob == "Any")
                            {
                                int randomJob = UnityEngine.Random.Range(0, 3);
                                realJob = (randomJob == 0 ? "Pilot" : (randomJob == 1 ? "Engineer" : "Scientist"));
                            }

                            // Perform the search loop at least once, and repeat until success or failure.
                            do
                            {
                                // Incremet attempts.
                                searchAttempts++;

                                // If the user list is empty,
                                if (usersInChat.Count == 0)
                                {
                                    // No viewers left.
                                    failedToFindOne = true;
                                }
                                // Otherwise, continue attempting to draft.
                                else
                                {
                                    // Gets a random user from the list.
                                    string userDrafted = usersInChat[UnityEngine.Random.Range(0, usersInChat.Count)];

                                    // Creates a new Unity web request (WWW) using the user chosen.
                                    Hashtable headers = new Hashtable();
                                    headers.Add("Client-ID", clientID);
                                    WWW getUser = new WWW("https://api.twitch.tv/helix/users?login=" + userDrafted, null, headers);

                                    // Waits for the web request to finish.
                                    yield return getUser;

                                    // Check for errors.
                                    if (string.IsNullOrEmpty(getUser.error))
                                    {
                                        // Parses the real username of the chosen user.
                                        realUsername = getUser.text.Substring(getUser.text.IndexOf("\"display_name\""));
                                        realUsername = realUsername.Substring(realUsername.IndexOf(":") + 2);
                                        realUsername = realUsername.Substring(0, realUsername.IndexOf(",") - 1);

                                        oddUsername = userDrafted;
                                        foundProperKerbal = true;
                                    }
                                    // If there is an error,
                                    else
                                    {
                                        // Invoke failure, stating web error.
                                        failure.Invoke("Web error: " + getUser.error);
                                    }
                                }
                            }
                            while (!foundProperKerbal && !failedToFindOne && searchAttempts < 25);

                            // If we found a Kerbal with the right job,
                            if (foundProperKerbal)
                            {
                                // If the save is not supressed,
                                if (!suppressSave)
                                {
                                    // Save the new user in the drawing file.
                                    Instance.AlreadyDrafted.Add(oddUsername);
                                }

                                Dictionary<string, string> drafted = new Dictionary<string, string>();

                                drafted.Add("name", RealUserName(realUsername));
                                drafted.Add("job", realJob);

                                Log.Info("Adding drafted user: " + realUsername);
                                // Invoke the success Action, allowing the caller to continue.
                                success.Invoke(drafted);
                            }
                            // Else, if we failed to find one,
                            else if (failedToFindOne)
                            {
                                // Invoke the failure Action, allowing the caller to handle this error.
                                failure.Invoke("Can't draft! No more valid users.");
                            }
                            // Else, if the search was attempted too many times,
                            else if (searchAttempts >= 25)
                            {
                                // Invoke the failure Action, allowing the caller to handle this error.
                                failure.Invoke("Can't draft! Too many attempts.");
                            }
                        }
                    }
                }
                // If there is an error,
                else
                {
                    // Invoke failure, stating web error.
                    failure.Invoke("Web error: " + getList.error);
                }
            }
        }

        #endregion

        #region Saving

        /// <summary>
        /// Saves the global settings used in this class.
        /// </summary>
        public void SaveGlobalSettings()
        {
            // Load global settings.
            ConfigNode globalSettings = ConfigNode.Load(settingsLocation + "GlobalSettings.cfg");
            // If the file exists,
            if (globalSettings != null)
            {
                // Get the draft settings node.
                ConfigNode draftSettings = globalSettings.GetNode("DRAFT");

                // If the DRAFT node doesn't exist,
                if (draftSettings == null)
                {
                    // Create a new DRAFT node to write to.
                    draftSettings = globalSettings.AddNode("DRAFT");
                }

                // Write the Channel and AddKerman values to it.
                if (draftSettings.HasValue("channel")) { draftSettings.SetValue("channel", channel); } else { draftSettings.AddValue("channel", channel); }
                if (draftSettings.HasValue("addKerman")) { draftSettings.SetValue("addKerman", addKerman.ToString()); } else { draftSettings.AddValue("addKerman", addKerman.ToString()); }

                // Get the list of bots to remove.
                ConfigNode botSettings = globalSettings.GetNode("BOTS");

                // If the BOTS node exists,
                if (botSettings != null)
                {
                    // Remove it so it can be replaced.
                    globalSettings.RemoveNode(botSettings);
                }

                // Create a new BOTS node to write to.
                botSettings = globalSettings.AddNode("BOTS");

                // For each user in the BotsToRemove list,
                foreach (string bot in BotsToRemove)
                {
                    // Create a new BOT node and assign the bot name to it.
                    ConfigNode botNode = botSettings.AddNode("BOT");
                    botNode.AddValue("name", bot);
                }

                // Save the file.
                globalSettings.Save(settingsLocation + "GlobalSettings.cfg");
            }
            // If the file doesn't exist,
            else
            {
                // Log a warning that it wasn't found.
                Logger.DebugWarning("(During save) GlobalSettings.cfg wasn't found. Generating to save global settings.");

                // Create a new root node.
                ConfigNode root = new ConfigNode();

                // Create a new DRAFT node to write the general settings to.
                ConfigNode draftSettings = root.AddNode("DRAFT");

                draftSettings.AddValue("channel", channel);
                draftSettings.AddValue("addKerman", addKerman.ToString());

                // Create a new BOTS node to write to.
                ConfigNode botSettings = root.AddNode("BOTS");

                // For each user in the BotsToRemove list,
                foreach (string bot in BotsToRemove)
                {
                    // Create a new BOT node and assign the bot name to it.
                    ConfigNode botNode = botSettings.AddNode("BOT");
                    botNode.AddValue("name", bot);
                }

                // Save the file.
                root.Save(settingsLocation + "GlobalSettings.cfg");
            }
        }

        /// <summary>
        /// Saves the list of drawn users.
        /// </summary>
        public void SaveDrawn()
        {
            // Load global settings.
            ConfigNode globalSettings = ConfigNode.Load(settingsLocation + "GlobalSettings.cfg");
            // If the file exists,
            if (globalSettings != null)
            {
                // Get the list of users which have already won a drawing.
                ConfigNode drawnSettings = globalSettings.GetNode("DRAWN");

                // If the DRAWN node exists,
                if (drawnSettings != null)
                {
                    // Remove it so it can be replaced.
                    globalSettings.RemoveNode(drawnSettings);
                }

                // Create a new DRAWN node to write to.
                drawnSettings = globalSettings.AddNode("DRAWN");

                // For each user in the DrawnUsers list,
                foreach (string drawn in DrawnUsers)
                {
                    // Create a new USER node and assign the username to it.
                    ConfigNode drawnNode = drawnSettings.AddNode("USER");
                    drawnNode.AddValue("name", drawn);
                }

                // Save the file.
                globalSettings.Save(settingsLocation + "GlobalSettings.cfg");
            }
            // If the file doesn't exist,
            else
            {
                // Log a warning that it wasn't found.
                Logger.DebugWarning("(During save) GlobalSettings.cfg wasn't found. Generating to save drawn users.");

                // Create a new root node.
                ConfigNode root = new ConfigNode();
                // Create a new DRAWN node to write to.
                ConfigNode drawnSettings = root.AddNode("DRAWN");

                // For each user in the DrawnUsers list,
                foreach (string drawn in DrawnUsers)
                {
                    // Create a new USER node and assign the username to it.
                    ConfigNode drawnNode = drawnSettings.AddNode("USER");
                    drawnNode.AddValue("name", drawn);
                }

                // Save the file.
                root.Save(settingsLocation + "GlobalSettings.cfg");
            }
        }

        /// <summary>
        /// Converts the specified Kerbal name back to a viewer username, and saves it.
        /// </summary>
        /// <param name="kerbalName">The Kerbal name.</param>
        public static void SaveSupressedDraft(string kerbalName)
        {
            // copy the name for modification.
            string newName = kerbalName;

            // If " Kerman" is in the name,
            if (newName.Contains(" Kerman"))
            {
                // Remove it!
                newName = newName.Split(' ')[0];
            }

            // Set the name to all lowercase.
            newName = newName.ToLower();

            // Save the supressed name.
            Instance.AlreadyDrafted.Add(newName);
        }

        #endregion

        #region Misc Functions

        /// <summary>
        /// Converts a twitch chatter request into a list of users.
        /// </summary>
        /// <param name="toParse">The request to parse.</param>
        /// <param name="parsingFrom">The section of the request to parse.</param>
        /// <returns>A list of users.</returns>
        string[] ParseIntoNameArray(string toParse, string parsingFrom)
        {
            string toSplit = toParse.Substring(toParse.IndexOf("\"" + parsingFrom + "\": ["));
            toSplit = toSplit.Substring(toSplit.IndexOf('[') + 1);
            toSplit = toSplit.Substring(0, toSplit.IndexOf(']'));
            if (toSplit == "")
            {
                return new string[] { };
            }
            toSplit = toSplit.Replace(" ", "");
            toSplit = toSplit.Replace("\n", "");
            string[] toRet = toSplit.Split(',');

            for (int i = 0; i < toRet.Length; i++)
            {
                toRet[i] = toRet[i].Substring(1, toRet[i].Length - 2);
            }

            return toRet;
        }

        #endregion

        #region Regexes

        /// <summary>
        /// A list of regex strings.
        /// </summary>
        string[] regexStrings = new string[] {

            "(?<!c)(?:a|4)n(?:a|4)(?:l|i|1)",
            "(?:a|4)nu(?:s|5)",
            "(?:a|4)r(?:s|5)(?:e|3)",
            "(?:a|4)(?:s|5)(?:s|5)",
            "b(?:a|4)(?:l|i|1)(?:l|i|1)(?:s|5)",
            "b(?:a|4)(?:s|5)(?:t|7)(?:a|4)rd",
            "b(?:l|i|1)(?:t|7)ch",
            "b(?:l|i|1)(?:a|4)(?:t|7)ch",
            "b(?:l|i|1)(?:o|0)(?:o|0)dy",
            "b(?:l|i|1)(?:o|0)wj(?:o|0)b",
            "b(?:o|0)(?:l|i|1)(?:l|i|1)(?:o|0)ck",
            "b(?:o|0)(?:l|i|1)(?:l|i|1)(?:o|0)k",
            "b(?:o|0)n(?:e|3)r",
            "b(?:o|0)(?:o|0)b",
            "bum",
            "bu(?:t|7)(?:t|7)",
            "c(?:l|i|1)(?:l|i|1)(?:t|7)",
            "c(?:o|0)ck",
            "c(?:o|0)(?:o|0)n",
            "cr(?:a|4)p",
            "cun(?:t|7)",
            "d(?:a|4)mn",
            "d(?:l|i|1)ck",
            "d(?:l|i|1)(?:l|i|1)d(?:o|0)",
            "dyk(?:e|3)",
            "(?:e|3)r(?:o|0)(?:t|7)(?:l|i|1)c",
            "f(?:a|4)g",
            "f(?:e|3)ck",
            "f(?:e|3)(?:l|i|1)(?:l|i|1)(?:a|4)(?:t|7)",
            "f(?:e|3)(?:l|i|1)ch",
            "fuck",
            "fudg(?:e|3)p(?:a|4)ck",
            "f(?:l|i|1)(?:a|4)ng(?:e|3)",
            "h(?:e|3)(?:l|i|1)(?:l|i|1)",
            "h(?:l|i|1)(?:t|7)(?:l|i|1)(?:e|3)r",
            "h(?:o|0)m(?:o|0)",
            "j(?:e|3)rk",
            "j(?:l|i|1)zz",
            "kn(?:o|0)b(?:e|3)nd",
            "(?:l|i|1)(?:a|4)b(?:l|i|1)(?:a|4)",
            "(?:l|i|1)m(?:a|4)(?:o|0)",
            "(?:l|i|1)mf(?:a|4)(?:o|0)",
            "muff",
            "n(?:l|i|1)gg(?:(?:e|3)r|(?:a|4))",
            "(?:o|0)mg",
            "p(?:e|3)n(?:l|i|1)(?:s|5)",
            "p(?:l|i|1)(?:s|5)(?:s|5)",
            "p(?:o|0)(?:o|0)p",
            "pr(?:l|i|1)ck",
            "pub(?:e|3)",
            "pu(?:s|5)(?:s|5)y",
            "qu(?:e|3)(?:e|3)r",
            "(?:s|5)(?:a|4)(?:t|7)(?:a|4)n",
            "(?:s|5)cr(?:o|0)(?:t|7)um",
            "(?:s|5)(?:e|3)x",
            "(?:s|5)h(?:l|i|1)(?:t|7)",
            "(?:s|5)(?:l|i|1)u(?:t|7)",
            "(?:s|5)m(?:e|3)gm(?:a|4)",
            "(?:s|5)punk",
            "(?:t|7)(?:l|i|1)(?:t|7)",
            "(?:t|7)(?:o|0)(?:s|5)(?:s|5)(?:e|3)r",
            "(?:t|7)urd",
            "(?:t|7)w(?:a|4)(?:t|7)",
            "v(?:a|4)g(?:l|i|1)n(?:a|4)",
            "w(?:a|4)nk",
            "wh(?:o|0)r(?:e|3)",
            "w(?:t|7)f"
        };

        /// <summary>
        /// A list of regexes which will be used to test drafted names.
        /// </summary>
        Regex[] regexes;

        /// <summary>
        /// Initializes the array of regexes.
        /// </summary>
        void InitRegexes()
        {
            List<Regex> rList = new List<Regex>();

            foreach (string r in regexStrings)
            {
                Regex rNew = new Regex(r, RegexOptions.IgnoreCase);
                rList.Add(rNew);
            }

            regexes = rList.ToArray();
        }

        #endregion
    }
}
