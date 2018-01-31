// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2017 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:    Lypyl (lypyldf@gmail.com)
// 
// Notes:
//

using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using FullSerializer;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.Banking;

namespace DaggerfallWorkshop.Game.Serialization
{
    /// <summary>
    /// Implements save/load logic.
    /// Games are saved in PersistentDataPath\Saves.
    /// Each save game will have a screenshot and multiple files.
    /// </summary>
    public class SaveLoadManager : MonoBehaviour
    {
        #region Fields

        const int latestSaveVersion = 1;

        const string rootSaveFolder = "Saves";
        const string savePrefix = "SAVE";
        const string quickSaveName = "QuickSave";
        const string autoSaveName = "AutoSave";
        const string saveInfoFilename = "SaveInfo.txt";
        const string saveDataFilename = "SaveData.txt";
        const string factionDataFilename = "FactionData.txt";
        const string containerDataFilename = "ContainerData.txt";
        const string questDataFilename = "QuestData.txt";
        const string discoveryDataFilename = "DiscoveryData.txt";
        const string conversationDataFilename = "ConversationData.txt";
        const string automapDataFilename = "AutomapData.txt";
        const string screenshotFilename = "Screenshot.jpg";
        const string notReadyExceptionText = "SaveLoad not ready.";

        // Serializable state manager for stateful game objects
        SerializableStateManager stateManager = new SerializableStateManager();

        // Enumerated save info
        Dictionary<int, string> enumeratedSaveFolders = new Dictionary<int, string>();
        Dictionary<int, SaveInfo_v1> enumeratedSaveInfo = new Dictionary<int, SaveInfo_v1>();
        Dictionary<string, List<int>> enumeratedCharacterSaves = new Dictionary<string, List<int>>();

        string unitySavePath = string.Empty;
        string daggerfallSavePath = string.Empty;
        bool loadInProgress = false;

        #endregion

        #region Properties

        public static SerializableStateManager StateManager
        {
            get { return Instance.stateManager; }
        }

        public int LatestSaveVersion
        {
            get { return latestSaveVersion; }
        }

        public string UnitySavePath
        {
            get { return GetUnitySavePath(); }
        }

        public string DaggerfallSavePath
        {
            get { return GetDaggerfallSavePath(); }
        }

        public int CharacterCount
        {
            get { return enumeratedCharacterSaves.Count; }
        }

        public string[] CharacterNames
        {
            get { return GetCharacterNames(); }
        }

        public bool LoadInProgress
        {
            get { return loadInProgress; }
        }

        #endregion
        
        #region Singleton

        static SaveLoadManager instance = null;
        public static SaveLoadManager Instance
        {
            get
            {
                if (instance == null)
                {
                    if (!FindSingleton(out instance))
                        return null;
                }
                return instance;
            }
        }

        public static bool HasInstance
        {
            get { return (instance != null); }
        }

        #endregion

        #region Unity

        void Awake()
        {
            sceneUnloaded = false;
        }

        void Start()
        {
            SetupSingleton();

            // Init classic game startup time at startup
            // This will also be modified when deserializing save game data
            DaggerfallUnity.Instance.WorldTime.Now.SetClassicGameStartTime();
        }

        static bool sceneUnloaded = false;
        void OnApplicationQuit()
        {
            sceneUnloaded = true;
        }

        void OnDestroy()
        {
            sceneUnloaded = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Checks if save/load system is ready.
        /// </summary>
        /// <returns>True if ready.</returns>
        public bool IsReady()
        {
            if (!DaggerfallUnity.Instance.IsReady || !DaggerfallUnity.Instance.IsPathValidated)
                return false;

            return true;
        }

        /// <summary>
        /// Updates save game enumerations.
        /// Must call this before working with existing saves.
        /// For example, this is called in save UI every time window pushed to stack.
        /// </summary>
        public void EnumerateSaves()
        {
            enumeratedSaveFolders = EnumerateSaveFolders();
            enumeratedSaveInfo = EnumerateSaveInfo(enumeratedSaveFolders);
            enumeratedCharacterSaves = EnumerateCharacterSaves(enumeratedSaveInfo);
        }

        /// <summary>
        /// Gets array of save keys for the specified character.
        /// </summary>
        /// <param name="characterName">Name of character.</param>
        /// <returns>Array of save keys, excluding </returns>
        public int[] GetCharacterSaveKeys(string characterName)
        {
            if (!enumeratedCharacterSaves.ContainsKey(characterName))
                return new int[0];

            return enumeratedCharacterSaves[characterName].ToArray();
        }

        public string[] GetCharacterNames()
        {
            List<string> names = new List<string>();
            foreach(var kvp in enumeratedCharacterSaves)
            {
                names.Add(kvp.Key);
            }

            return names.ToArray();
        }

        /// <summary>
        /// Gets folder containing save by key.
        /// </summary>
        /// <param name="key">Save key.</param>
        /// <returns>Path to save folder or empty string if key not found.</returns>
        public string GetSaveFolder(int key)
        {
            if (!enumeratedSaveFolders.ContainsKey(key))
                return string.Empty;

            return enumeratedSaveFolders[key];
        }

        /// <summary>
        /// Gets save information by key.
        /// </summary>
        /// <param name="key">Save key.</param>
        /// <returns>SaveInfo populated with save details, or empty struct if save not found.</returns>
        public SaveInfo_v1 GetSaveInfo(int key)
        {
            if (!enumeratedSaveInfo.ContainsKey(key))
                return new SaveInfo_v1();

            return enumeratedSaveInfo[key];
        }

        public Texture2D GetSaveScreenshot(int key)
        {
            if (!enumeratedSaveFolders.ContainsKey(key))
                return null;

            string path = Path.Combine(GetSaveFolder(key), screenshotFilename);
            byte[] data = File.ReadAllBytes(path);

            Texture2D screenshot = new Texture2D(0, 0);
            if (screenshot.LoadImage(data))
                return screenshot;

            return null;
        }

        /// <summary>
        /// Finds existing save folder.
        /// </summary>
        /// <param name="characterName">Name of character to match.</param>
        /// <param name="saveName">Name of save to match.</param>
        /// <returns>Save key or -1 if save not found.</returns>
        public int FindSaveFolderByNames(string characterName, string saveName)
        {
            int[] saves = GetCharacterSaveKeys(characterName);
            foreach (int key in saves)
            {
                SaveInfo_v1 compareInfo = GetSaveInfo(key);
                if (compareInfo.characterName == characterName &&
                    compareInfo.saveName == saveName)
                {
                    return key;
                }
            }

            return -1;
        }

        /// <summary>
        /// Finds most recent save.
        /// </summary>
        /// <returns>Save key of most recent save, or -1 if no saves found.</returns>
        public int FindMostRecentSave()
        {
            long mostRecentTime = -1;
            int mostRecentKey = -1;
            foreach (var kvp in enumeratedSaveInfo)
            {
                if (kvp.Value.dateAndTime.realTime > mostRecentTime)
                {
                    mostRecentTime = kvp.Value.dateAndTime.realTime;
                    mostRecentKey = kvp.Key;
                }
            }

            return mostRecentKey;
        }

        /// <summary>
        /// Deletes save folder.
        /// </summary>
        /// <param name="key">Save key.</param>
        public void DeleteSaveFolder(int key)
        {
            if (!enumeratedSaveFolders.ContainsKey(key))
                return;

            // For safety only delete known save files - do not perform a recursive delete
            // This way we don't blow up folder if user has placed something custom inside
            string path = GetSaveFolder(key);
            File.Delete(Path.Combine(path, saveDataFilename));
            File.Delete(Path.Combine(path, saveInfoFilename));
            File.Delete(Path.Combine(path, screenshotFilename));
            File.Delete(Path.Combine(path, containerDataFilename));
            File.Delete(Path.Combine(path, automapDataFilename));

            // Attempt to delete path itself
            // Even if delete fails path should be invalid with save info removed
            // Folder index will be excluded from enumeration and recycled later
            try
            {
                Directory.Delete(path);
            }
            catch(Exception ex)
            {
                string message = string.Format("Could not delete save folder '{0}'. Exception message: {1}", path, ex.Message);
                DaggerfallUnity.LogMessage(message);
            }

            // Update saves
            EnumerateSaves();
        }

        public void Save(string characterName, string saveName)
        {
            // Must be ready
            if (!IsReady())
                throw new Exception(notReadyExceptionText);

            // Look for existing save with this character and name
            int key = FindSaveFolderByNames(characterName, saveName);

            // Get or create folder
            string path;
            if (key == -1)
                path = CreateNewSavePath(enumeratedSaveFolders);
            else
                path = GetSaveFolder(key);

            // Save game
            StartCoroutine(SaveGame(saveName, path));
        }

        public void QuickSave()
        {
            Save(GameManager.Instance.PlayerEntity.Name, quickSaveName);
        }

        public void Load(int key)
        {
            // Must be ready
            if (!IsReady())
                throw new Exception(notReadyExceptionText);

            // Load must not be in progress
            if (loadInProgress)
                return;

            // Get folder
            string path;
            if (key == -1)
                return;
            else
                path = GetSaveFolder(key);

            // Load game
            loadInProgress = true;
            GameManager.Instance.PauseGame(false);
            StartCoroutine(LoadGame(path));

            // Notify
            DaggerfallUI.Instance.PopupMessage(HardStrings.gameLoaded);
        }

        public void Load(string characterName, string saveName)
        {
            //// Must be ready
            //if (!IsReady())
            //    throw new Exception(notReadyExceptionText);

            //// Load must not be in progress
            //if (loadInProgress)
            //    return;

            // Look for existing save with this character and name
            int key = FindSaveFolderByNames(characterName, saveName);
            Load(key);

            //// Get folder
            //string path;
            //if (key == -1)
            //    return;
            //else
            //    path = GetSaveFolder(key);

            //// Load game
            //loadInProgress = true;
            //GameManager.Instance.PauseGame(false);
            //StartCoroutine(LoadGame(path));

            //// Notify
            //DaggerfallUI.Instance.PopupMessage(HardStrings.gameLoaded);
        }

        public void QuickLoad()
        {
            Load(GameManager.Instance.PlayerEntity.Name, quickSaveName);
        }

        /// <summary>
        /// Checks if quick save folder exists.
        /// </summary>
        /// <returns>True if quick save exists.</returns>
        public bool HasQuickSave(string characterName)
        {
            // Look for existing save with this character and name
            int key = FindSaveFolderByNames(characterName, quickSaveName);

            // Get folder
            return key != -1;
        }

        #endregion

        #region Public Static Methods

        public static bool FindSingleton(out SaveLoadManager singletonOut)
        {
            singletonOut = FindObjectOfType<SaveLoadManager>();
            return singletonOut != null;
        }

        /// <summary>
        /// Register ISerializableGameObject with SerializableStateManager.
        /// </summary>
        public static void RegisterSerializableGameObject(ISerializableGameObject serializableObject)
        {
            if (sceneUnloaded)
                return;
            Instance.stateManager.RegisterStatefulGameObject(serializableObject);
        }

        /// <summary>
        /// Deregister ISerializableGameObject from SerializableStateManager.
        /// </summary>
        public static void DeregisterSerializableGameObject(ISerializableGameObject serializableObject)
        {
            if (sceneUnloaded)
                return;
            Instance.stateManager.DeregisterStatefulGameObject(serializableObject);
        }

        /// <summary>
        /// Force deregister all ISerializableGameObject instances from SerializableStateManager.
        /// </summary>
        public static void DeregisterAllSerializableGameObjects(bool keepPlayer = true)
        {
            if (sceneUnloaded)
                return;
            Instance.stateManager.DeregisterAllStatefulGameObjects(keepPlayer);
            Debug.Log("Deregistered all stateful objects");
        }

        /// <summary>
        /// Stores the current scene in the SerializableStateManager cache using the given name.
        /// </summary>
        public static void CacheScene(string sceneName)
        {
            if (!sceneUnloaded)
                Instance.stateManager.CacheScene(sceneName);
        }

        /// <summary>
        /// Restores the current scene from the SerializableStateManager cache using the given name.
        /// </summary>
        public static void RestoreCachedScene(string sceneName)
        {
            if (!sceneUnloaded)
                Instance.StartCoroutine(Instance.RestoreCachedSceneNextFrame(sceneName));
        }

        private IEnumerator RestoreCachedSceneNextFrame(string sceneName)
        {
            // Wait another frame so everthing has a chance to register
            yield return new WaitForEndOfFrame();
            // Restore the scene from cache
            stateManager.RestoreCachedScene(sceneName);
        }

        /// <summary>
        /// Clears the SerializableStateManager scene cache.
        /// </summary>
        /// <param name="start">True if starting a new or loaded game, so also clear permanent scene list</param>
        public static void ClearSceneCache(bool start)
        {
            if (!sceneUnloaded)
                Instance.stateManager.ClearSceneCache(start);
        }

        #endregion

        #region Serialization Helpers

        static readonly fsSerializer _serializer = new fsSerializer();

        public static string Serialize(Type type, object value, bool pretty = true)
        {
            // Serialize the data
            fsData data;
            _serializer.TrySerialize(type, value, out data).AssertSuccessWithoutWarnings();

            // Emit the data via JSON
            return (pretty) ? fsJsonPrinter.PrettyJson(data) : fsJsonPrinter.CompressedJson(data);
        }

        public static object Deserialize(Type type, string serializedState)
        {
            // Step 1: Parse the JSON data
            fsData data = fsJsonParser.Parse(serializedState);

            // Step 2: Deserialize the data
            object deserialized = null;
            _serializer.TryDeserialize(data, type, ref deserialized).AssertSuccessWithoutWarnings();

            return deserialized;
        }

        #endregion

        #region Private Methods

        private void SetupSingleton()
        {
            if (instance == null)
                instance = this;
            else if (instance != this)
            {
                if (Application.isPlaying)
                {
                    DaggerfallUnity.LogMessage("Multiple SaveLoad instances detected in scene!", true);
                    Destroy(gameObject);
                }
            }
        }

        string GetUnitySavePath()
        {
            if (!string.IsNullOrEmpty(unitySavePath))
                return unitySavePath;

            string result = string.Empty;

            // Try settings
            result = DaggerfallUnity.Settings.MyDaggerfallUnitySavePath;
            if (string.IsNullOrEmpty(result) || !Directory.Exists(result))
            {
                // Default to dataPath
                result = Path.Combine(Application.persistentDataPath, rootSaveFolder);
                if (!Directory.Exists(result))
                {
                    // Attempt to create path
                    Directory.CreateDirectory(result);
                }
            }

            // Test result is a valid path
            if (!Directory.Exists(result))
                throw new Exception("Could not locate valid path for Unity save files. Check 'MyDaggerfallUnitySavePath' in settings.ini.");

            // Log result and save path
            DaggerfallUnity.LogMessage(string.Format("Using path '{0}' for Unity saves.", result), true);
            unitySavePath = result;

            return result;
        }

        string GetDaggerfallSavePath()
        {
            if (!string.IsNullOrEmpty(daggerfallSavePath))
                return daggerfallSavePath;

            string result = string.Empty;

            // Test result is a valid path
            result = Path.GetDirectoryName(DaggerfallUnity.Instance.Arena2Path);
            if (!Directory.Exists(result))
                throw new Exception("Could not locate valid path for Daggerfall save files. Check 'MyDaggerfallPath' in settings.ini points to your Daggerfall folder.");

            // Log result and save path
            DaggerfallUnity.LogMessage(string.Format("Using path '{0}' for Daggerfall save importing.", result), true);
            daggerfallSavePath = result;

            return result;
        }

        void WriteSaveFile(string path, string json)
        {
            File.WriteAllText(path, json);
        }

        string ReadSaveFile(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch(Exception ex)
            {
                DaggerfallUnity.LogMessage(ex.Message);
                return string.Empty;
            }
        }

        Dictionary<int, string> EnumerateSaveFolders()
        {
            // Get directories in save path matching prefix
            string[] directories = Directory.GetDirectories(UnitySavePath, savePrefix + "*", SearchOption.TopDirectoryOnly);

            // Build dictionary keyed by save index
            Dictionary<int, string> saveFolders = new Dictionary<int, string>();
            foreach (string directory in directories)
            {
                // Get everything right of prefix in folder name (should be a number)
                int key;
                string indexStr = Path.GetFileName(directory).Substring(savePrefix.Length);
                if (int.TryParse(indexStr, out key))
                {
                    // Must contain a save info file to be a valid save folder
                    if (File.Exists(Path.Combine(directory, saveInfoFilename)))
                        saveFolders.Add(key, directory);
                }
            }

            return saveFolders;
        }

        Dictionary<int, SaveInfo_v1> EnumerateSaveInfo(Dictionary<int, string> saveFolders)
        {
            Dictionary<int, SaveInfo_v1> saveInfoDict = new Dictionary<int, SaveInfo_v1>();
            foreach (var kvp in saveFolders)
            {
                try
                {
                    SaveInfo_v1 saveInfo = ReadSaveInfo(kvp.Value);
                    saveInfoDict.Add(kvp.Key, saveInfo);
                }
                catch (Exception ex)
                {
                    DaggerfallUnity.LogMessage(string.Format("Failed to read {0} in save folder {1}. Exception.Message={2}", saveInfoFilename, kvp.Value, ex.Message));
                }
            }

            return saveInfoDict;
        }

        Dictionary<string, List<int>> EnumerateCharacterSaves(Dictionary<int, SaveInfo_v1> saveInfo)
        {
            Dictionary<string, List<int>> characterSaves = new Dictionary<string, List<int>>();
            foreach (var kvp in saveInfo)
            {
                // Add character to name dictionary
                if (!characterSaves.ContainsKey(kvp.Value.characterName))
                {
                    characterSaves.Add(kvp.Value.characterName, new List<int>());
                }

                // Add save key to character save list
                characterSaves[kvp.Value.characterName].Add(kvp.Key);
            }

            return characterSaves;
        }

        SaveInfo_v1 ReadSaveInfo(string saveFolder)
        {
            string saveInfoJson = ReadSaveFile(Path.Combine(saveFolder, saveInfoFilename));
            SaveInfo_v1 saveInfo = Deserialize(typeof(SaveInfo_v1), saveInfoJson) as SaveInfo_v1;

            return saveInfo;
        }

        /// <summary>
        /// Checks if save folder exists.
        /// </summary>
        /// <param name="folderName">Folder name of save.</param>
        /// <returns>True if folder exists.</returns>
        bool HasSaveFolder(string folderName)
        {
            return Directory.Exists(Path.Combine(UnitySavePath, folderName));
        }

        #endregion

        #region Saving

        SaveData_v1 BuildSaveData()
        {
            SaveData_v1 saveData = new SaveData_v1();
            saveData.header = new SaveDataDescription_v1();
            saveData.currentUID = DaggerfallUnity.CurrentUID;
            saveData.dateAndTime = GetDateTimeData();
            saveData.playerData = stateManager.GetPlayerData();
            saveData.dungeonData = GetDungeonData();
            saveData.enemyData = stateManager.GetEnemyData();
            saveData.lootContainers = stateManager.GetLootContainerData();
            saveData.bankAccounts = GetBankAccountData();
            saveData.bankDeeds = GetBankDeedData();
            saveData.escortingFaces = DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces.GetSaveData();
            saveData.sceneCache = stateManager.GetSceneCache();

            return saveData;
        }

        DateAndTime_v1 GetDateTimeData()
        {
            DateAndTime_v1 data = new DateAndTime_v1();
            data.gameTime = DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToSeconds();
            data.realTime = DateTime.Now.Ticks;

            return data;
        }

        DungeonData_v1 GetDungeonData()
        {
            DungeonData_v1 data = new DungeonData_v1();
            data.actionDoors = stateManager.GetActionDoorData();
            data.actionObjects = stateManager.GetActionObjectData();

            return data;
        }

        BankRecordData_v1[] GetBankAccountData()
        {
            List<BankRecordData_v1> records = new List<BankRecordData_v1>();

            foreach (var record in Banking.DaggerfallBankManager.BankAccounts)
            {
                if (record == null)
                    continue;
                else if (record.accountGold == 0 && record.loanTotal == 0 && record.loanDueDate == 0)
                    continue;
                else
                    records.Add(record);
            }

            return records.ToArray();
        }

        BankDeedData_v1 GetBankDeedData()
        {
            return new BankDeedData_v1() {
                shipType = (int) DaggerfallBankManager.OwnedShip,
/*                houseDeed = new HouseDeedData_v1() {
                    houseId = 1
                }*/
            };
        }

        /// <summary>
        /// Gets a specific save path.
        /// </summary>
        /// <param name="folderName">Folder name of save.</param>
        /// <param name="create">Creates folder if it does not exist.</param>
        /// <returns>Save path.</returns>
        string GetSavePath(string folderName, bool create)
        {
            // Compose folder path
            string path = Path.Combine(UnitySavePath, folderName);

            // Create directory if it does not exist
            if (create && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        /// <summary>
        /// Creates a new indexed save path.
        /// </summary>
        /// <param name="saveFolders">Save folder enumeration.</param>
        /// <returns>Save path.</returns>
        string CreateNewSavePath(Dictionary<int, string> saveFolders)
        {
            // Find first available save index in dictionary
            int key = 0;
            while (saveFolders.ContainsKey(key))
            {
                key++;
            }

            return GetSavePath(savePrefix + key, true);
        }

        #endregion

        #region Loading

        void RestoreSaveData(SaveData_v1 saveData)
        {
            DaggerfallUnity.CurrentUID = saveData.currentUID;
            RestoreDateTimeData(saveData.dateAndTime);
            stateManager.RestorePlayerData(saveData.playerData);
            RestoreDungeonData(saveData.dungeonData);
            stateManager.RestoreEnemyData(saveData.enemyData);
            stateManager.RestoreLootContainerData(saveData.lootContainers);
            RestoreBankData(saveData.bankAccounts);
            RestoreBankDeedData(saveData.bankDeeds);
            RestoreEscortingFacesData(saveData.escortingFaces);
            stateManager.RestoreSceneCache(saveData.sceneCache);
        }

        void RestoreDateTimeData(DateAndTime_v1 dateTimeData)
        {
            if (dateTimeData == null)
                return;

            DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.FromSeconds(dateTimeData.gameTime);
        }

        void RestoreDungeonData(DungeonData_v1 dungeonData)
        {
            if (dungeonData == null)
                return;

            stateManager.RestoreActionDoorData(dungeonData.actionDoors);
            stateManager.RestoreActionObjectData(dungeonData.actionObjects);
        }

        void RestoreBankData(BankRecordData_v1[] bankData)
        {
            DaggerfallBankManager.SetupAccounts();

            if (bankData == null)
                return;

            for (int i = 0; i < bankData.Length; i++)
            {
                if (bankData[i].regionIndex < 0 || bankData[i].regionIndex >= DaggerfallBankManager.BankAccounts.Length)
                    continue;

                DaggerfallBankManager.BankAccounts[bankData[i].regionIndex] = bankData[i];
            }
        }

        void RestoreBankDeedData(BankDeedData_v1 deedData)
        {
            if (deedData == null)
                return;

            DaggerfallBankManager.OwnedShip = (ShipType) deedData.shipType;
        }

        void RestoreEscortingFacesData(FaceDetails[] escortingFaces)
        {
            if (escortingFaces == null)
                DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces.ClearFaces();
            else
                DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces.RestoreSaveData(escortingFaces);
        }

        #endregion

        #region Utility

        IEnumerator SaveGame(string saveName, string path)
        {
            // Build save data
            SaveData_v1 saveData = BuildSaveData();

            // Build save info
            SaveInfo_v1 saveInfo = new SaveInfo_v1();
            saveInfo.saveVersion = LatestSaveVersion;
            saveInfo.saveName = saveName;
            saveInfo.characterName = saveData.playerData.playerEntity.name;
            saveInfo.dateAndTime = saveData.dateAndTime;

            // Build faction data
            FactionData_v2 factionData = stateManager.GetPlayerFactionData();

            // Build quest data
            QuestMachine.QuestMachineData_v1 questData = QuestMachine.Instance.GetSaveData();

            // Get discovery data
            Dictionary<int, PlayerGPS.DiscoveredLocation> discoveryData = GameManager.Instance.PlayerGPS.GetDiscoverySaveData();

            // Get conversation data
            TalkManager.SaveDataConversation conversationData = GameManager.Instance.TalkManager.GetConversationSaveData();

            // Serialize save data to JSON strings
            string saveDataJson = Serialize(saveData.GetType(), saveData);
            string saveInfoJson = Serialize(saveInfo.GetType(), saveInfo);
            string factionDataJson = Serialize(factionData.GetType(), factionData);
            string questDataJson = Serialize(questData.GetType(), questData);
            string discoveryDataJson = Serialize(discoveryData.GetType(), discoveryData);
            string conversationDataJson = Serialize(conversationData.GetType(), conversationData);

            // Create screenshot for save
            // TODO: Hide UI for screenshot or use a different method
            yield return new WaitForEndOfFrame();
            Texture2D screenshot = new Texture2D(Screen.width, Screen.height);
            screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            screenshot.Apply();

            // Save data to files
            WriteSaveFile(Path.Combine(path, saveDataFilename), saveDataJson);
            WriteSaveFile(Path.Combine(path, saveInfoFilename), saveInfoJson);
            WriteSaveFile(Path.Combine(path, factionDataFilename), factionDataJson);
            WriteSaveFile(Path.Combine(path, questDataFilename), questDataJson);
            WriteSaveFile(Path.Combine(path, discoveryDataFilename), discoveryDataJson);
            WriteSaveFile(Path.Combine(path, conversationDataFilename), conversationDataJson);

            // Save automap state
            try
            {
                Dictionary<string, DaggerfallAutomap.AutomapGeometryDungeonState> automapState = GameManager.Instance.InteriorAutomap.GetState();
                string automapDataJson = Serialize(automapState.GetType(), automapState);
                WriteSaveFile(Path.Combine(path, automapDataFilename), automapDataJson);
            }
            catch(Exception ex)
            {
                string message = string.Format("Failed to save automap state. Message: {0}", ex.Message);
                Debug.Log(message);
            }

            // Save screenshot
            byte[] bytes = screenshot.EncodeToJPG();
            File.WriteAllBytes(Path.Combine(path, screenshotFilename), bytes);

            // Raise OnSaveEvent
            RaiseOnSaveEvent(saveData);

            // Notify
            DaggerfallUI.Instance.PopupMessage(HardStrings.gameSaved);
        }

        IEnumerator LoadGame(string path)
        {
            GameManager.Instance.PlayerDeath.ClearDeathAnimation();
            GameManager.Instance.PlayerMotor.CancelMovement = true;
            InputManager.Instance.ClearAllActions();
            QuestMachine.Instance.ClearState();
            stateManager.ClearSceneCache();

            // Read save data from files
            string saveDataJson = ReadSaveFile(Path.Combine(path, saveDataFilename));
            string factionDataJson = ReadSaveFile(Path.Combine(path, factionDataFilename));
            string questDataJson = ReadSaveFile(Path.Combine(path, questDataFilename));
            string discoveryDataJson = ReadSaveFile(Path.Combine(path, discoveryDataFilename));
            string conversationDataJson = ReadSaveFile(Path.Combine(path, conversationDataFilename));

            // Deserialize JSON strings
            SaveData_v1 saveData = Deserialize(typeof(SaveData_v1), saveDataJson) as SaveData_v1;

            // Must have a serializable player
            if (!stateManager.SerializablePlayer)
                yield break;

            // Call start load event
            RaiseOnStartLoadEvent(saveData);

            // Immediately set date so world is loaded with correct season
            RestoreDateTimeData(saveData.dateAndTime);

            // Restore discovery data
            if (!string.IsNullOrEmpty(discoveryDataJson))
            {
                Dictionary<int, PlayerGPS.DiscoveredLocation> discoveryData = Deserialize(typeof(Dictionary<int, PlayerGPS.DiscoveredLocation>), discoveryDataJson) as Dictionary<int, PlayerGPS.DiscoveredLocation>;
                GameManager.Instance.PlayerGPS.RestoreDiscoveryData(discoveryData);
            }
            else
            {
                // Clear discovery data when not in save, or live state will be retained from previous session
                GameManager.Instance.PlayerGPS.ClearDiscoveryData();
            }

            // Restore conversation data
            if (!string.IsNullOrEmpty(conversationDataJson))
            {
                TalkManager.SaveDataConversation conversationData = Deserialize(typeof(TalkManager.SaveDataConversation), conversationDataJson) as TalkManager.SaveDataConversation;
                GameManager.Instance.TalkManager.RestoreConversationData(conversationData);
            }
            else
            {                
                GameManager.Instance.TalkManager.RestoreConversationData(null);
            }

            // Must have PlayerEnterExit to respawn player at saved location
            PlayerEnterExit playerEnterExit = stateManager.SerializablePlayer.GetComponent<PlayerEnterExit>();
            if (!playerEnterExit)
                yield break;

            // Check exterior doors are included in save, we need these to exit building
            bool hasExteriorDoors;
            if (saveData.playerData.playerPosition.exteriorDoors == null || saveData.playerData.playerPosition.exteriorDoors.Length == 0)
                hasExteriorDoors = false;
            else
                hasExteriorDoors = true;

            // Restore building summary early for interior layout code
            if (saveData.playerData.playerPosition.insideBuilding)
                playerEnterExit.BuildingDiscoveryData = saveData.playerData.playerPosition.buildingDiscoveryData;

            // Restore faction data to player entity
            // This is done early as later objects may require faction information on restore
            if (!string.IsNullOrEmpty(factionDataJson))
            {
                FactionData_v2 factionData = Deserialize(typeof(FactionData_v2), factionDataJson) as FactionData_v2;
                stateManager.RestoreFactionData(factionData);
                Debug.Log("LoadGame() restored faction state from save.");
            }
            else
            {
                Debug.Log("LoadGame() did not find saved faction data. Player will resume with default faction state.");
            }

            // Restore quest machine state
            if (!string.IsNullOrEmpty(questDataJson))
            {
                QuestMachine.QuestMachineData_v1 questData = Deserialize(typeof(QuestMachine.QuestMachineData_v1), questDataJson) as QuestMachine.QuestMachineData_v1;
                QuestMachine.Instance.RestoreSaveData(questData);
            }

            // Raise reposition flag if terrain sampler changed
            // This is required as changing terrain samplers will invalidate serialized player coordinates
            bool repositionPlayer = false;
            if (saveData.playerData.playerPosition.terrainSamplerName != DaggerfallUnity.Instance.TerrainSampler.ToString() ||
                saveData.playerData.playerPosition.terrainSamplerVersion != DaggerfallUnity.Instance.TerrainSampler.Version)
            {
                repositionPlayer = true;
                if (DaggerfallUI.Instance.DaggerfallHUD != null)
                    DaggerfallUI.Instance.DaggerfallHUD.PopupText.AddText("Terrain sampler changed. Repositioning player.");
            }

            // Raise reposition flag if player is supposed to start indoors but building has no doors
            if (saveData.playerData.playerPosition.insideBuilding && !hasExteriorDoors)
            {
                repositionPlayer = true;
                if (DaggerfallUI.Instance.DaggerfallHUD != null)
                    DaggerfallUI.Instance.DaggerfallHUD.PopupText.AddText("Building has no exterior doors. Repositioning player.");
            }

            // Start the respawn process based on saved player location
            if (saveData.playerData.playerPosition.insideDungeon && !repositionPlayer)
            {
                // Start in dungeon
                playerEnterExit.RespawnPlayer(
                    saveData.playerData.playerPosition.worldPosX,
                    saveData.playerData.playerPosition.worldPosZ,
                    true,
                    false);
            }
            else if (saveData.playerData.playerPosition.insideBuilding && hasExteriorDoors && !repositionPlayer)
            {
                // Start in building
                playerEnterExit.RespawnPlayer(
                    saveData.playerData.playerPosition.worldPosX,
                    saveData.playerData.playerPosition.worldPosZ,
                    saveData.playerData.playerPosition.insideDungeon,
                    saveData.playerData.playerPosition.insideBuilding,
                    saveData.playerData.playerPosition.exteriorDoors);
            }
            else
            {
                // Start outside
                playerEnterExit.RespawnPlayer(
                    saveData.playerData.playerPosition.worldPosX,
                    saveData.playerData.playerPosition.worldPosZ,
                    false,
                    false,
                    null,
                    repositionPlayer);
            }

            // Smash to black while respawning
            DaggerfallUI.Instance.SmashHUDToBlack();

            // Keep yielding frames until world is ready again
            while (playerEnterExit.IsRespawning)
            {
                yield return new WaitForEndOfFrame();
            }

            // Wait another frame so everthing has a chance to register
            yield return new WaitForEndOfFrame();

            // Restore save data to objects in newly spawned world
            RestoreSaveData(saveData);

            // Load automap state
            try
            {
                string automapDataJson = ReadSaveFile(Path.Combine(path, automapDataFilename));
                Dictionary<string, DaggerfallAutomap.AutomapGeometryDungeonState> automapState = null;

                if (!string.IsNullOrEmpty(automapDataJson))
                    automapState = Deserialize(typeof(Dictionary<string, DaggerfallAutomap.AutomapGeometryDungeonState>), automapDataJson) as Dictionary<string, DaggerfallAutomap.AutomapGeometryDungeonState>;

                if (automapState != null)
                    GameManager.Instance.InteriorAutomap.SetState(automapState);
            }
            catch (Exception ex)
            {
                string message = string.Format("Failed to load automap state. Message: {0}", ex.Message);
                Debug.Log(message);
            }

            // Clear any orphaned quest items
            RemoveAllOrphanedQuestItems();

            // Lower load in progress flag
            loadInProgress = false;

            // Fade out from black
            DaggerfallUI.Instance.FadeHUDFromBlack(1.0f);

            // Raise OnLoad event
            RaiseOnLoadEvent(saveData);
        }

        /// <summary>
        /// Looks for orphaned quest items (quest no longer active) remaining in player item collections.
        /// </summary>
        void RemoveAllOrphanedQuestItems()
        {
            int count = 0;
            Entity.PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            count += playerEntity.Items.RemoveOrphanedQuestItems();
            count += playerEntity.WagonItems.RemoveOrphanedQuestItems();
            count += playerEntity.OtherItems.RemoveOrphanedQuestItems();
            if (count > 0)
            {
                Debug.LogFormat("Removed {0} orphaned quest items.", count);
            }
        }

        #endregion

        #region Events

        // OnSave
        public delegate void OnSaveEventHandler(SaveData_v1 saveData);
        public static event OnSaveEventHandler OnSave;
        protected virtual void RaiseOnSaveEvent(SaveData_v1 saveData)
        {
            if (OnSave != null)
                OnSave(saveData);
        }

        // OnStartLoad
        public delegate void OnStartLoadEventHandler(SaveData_v1 saveData);
        public static event OnStartLoadEventHandler OnStartLoad;
        protected virtual void RaiseOnStartLoadEvent(SaveData_v1 saveData)
        {
            if (OnStartLoad != null)
                OnStartLoad(saveData);
        }

        // OnLoad
        public delegate void OnLoadEventHandler(SaveData_v1 saveData);
        public static event OnLoadEventHandler OnLoad;
        protected virtual void RaiseOnLoadEvent(SaveData_v1 saveData)
        {
            if (OnLoad != null)
                OnLoad(saveData);
        }

        #endregion
    }
}