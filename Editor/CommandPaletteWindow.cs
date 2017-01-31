using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DTCommandPalette {
    public class CommandPaletteWindow : EditorWindow {
        // PRAGMA MARK - Constants
        private const string kTextFieldControlName = "CommandPaletteWindowTextField";

        private const int kMaxRowsDisplayed = 8;
        private const float kWindowWidth = 400.0f;
        private const float kWindowHeight = 30.0f;

        private const float kRowHeight = 32.0f;
        private const float kRowTitleHeight = 20.0f;
        private const float kRowSubtitleHeightPadding = -5.0f;
        private const float kRowSubtitleHeight = 15.0f;

        private const int kSubtitleMaxSoftLength = 35;
        private const int kSubtitleMaxTitleAdditiveLength = 15;

        private const float kIconEdgeSize = 15.0f;
        private const float kIconPadding = 7.0f;

        private const int kFontSize = 21;

        public static bool _debug = false;

        public static string _scriptDirectory = null;
        public static string ScriptDirectory {
            get { return ScriptableObjectEditorUtil.PathForScriptableObjectType<CommandPaletteWindow>(); }
        }


        // PRAGMA MARK - Public Interface
        [MenuItem("Window/Open.. %t")]
        public static void ShowObjectWindow() {
            var commandManager = new CommandManager();
            if (!Application.isPlaying) {
                commandManager.AddLoader(new PrefabAssetCommandLoader());
                commandManager.AddLoader(new SceneAssetCommandLoader());
            }
            commandManager.AddLoader(new SelectGameObjectCommandLoader());

            CommandPaletteWindow.InitializeWindow("Open.. ", commandManager);
        }

        [MenuItem("Window/Open Command Palette.. %#m")]
        public static void ShowCommandPaletteWindow() {
            var commandManager = new CommandManager();
            commandManager.AddLoader(new MethodCommandLoader());

            CommandPaletteWindow.InitializeWindow("Command Palette.. ", commandManager);
        }

        public static void InitializeWindow(string title, CommandManager commandManager, bool clearInput = false) {
            if (commandManager == null) {
                Debug.LogError("CommandPaletteWindow: Can't initialize a window without a command manager!");
                return;
            }

            if (clearInput) {
                _input = "";
            }

            EditorWindow window = EditorWindow.GetWindow(typeof(CommandPaletteWindow), utility: true, title: title, focus: true);
            window.position = new Rect(0.0f, 0.0f, CommandPaletteWindow.kWindowWidth, CommandPaletteWindow.kWindowHeight);
            window.CenterInMainEditorWindow();
            window.wantsMouseMove = true;

            CommandPaletteWindow._selectedIndex = 0;
            CommandPaletteWindow._focusTrigger = true;
            CommandPaletteWindow._isClosing = false;

            _commandManager = commandManager;
            CommandPaletteWindow.ReloadObjects();
        }

        // PRAGMA MARK - Internal
        protected static string _input = "";
        protected static bool _focusTrigger = false;
        protected static bool _isClosing = false;
        protected static int _selectedIndex = 0;
        protected static ICommand[] _objects = new ICommand[0];
        protected static CommandManager _commandManager = null;
        protected static Color _selectedBackgroundColor = ColorUtil.HexStringToColor("#4076d3").WithAlpha(0.4f);

        private static string _parsedSearchInput = "";
        private static string[] _parsedArguments = null;

        protected void OnGUI() {
            Event e = Event.current;
            switch (e.type) {
                case EventType.KeyDown:
                this.HandleKeyDownEvent(e);
                break;
                default:
                break;
            }

            if (CommandPaletteWindow._objects.Length > 0) {
                CommandPaletteWindow._selectedIndex = MathUtil.Wrap(CommandPaletteWindow._selectedIndex, 0, Mathf.Min(CommandPaletteWindow._objects.Length, CommandPaletteWindow.kMaxRowsDisplayed));
            } else {
                CommandPaletteWindow._selectedIndex = 0;
            }

            GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.fontSize = CommandPaletteWindow.kFontSize;

            GUI.SetNextControlName(CommandPaletteWindow.kTextFieldControlName);
            string updatedInput = EditorGUI.TextField(new Rect(0.0f, 0.0f, CommandPaletteWindow.kWindowWidth, CommandPaletteWindow.kWindowHeight), CommandPaletteWindow._input, textFieldStyle);
            if (updatedInput != CommandPaletteWindow._input) {
                CommandPaletteWindow._input = updatedInput;
                this.HandleInputUpdated();
            }

            int displayedAssetCount = Mathf.Min(CommandPaletteWindow._objects.Length, CommandPaletteWindow.kMaxRowsDisplayed);
            this.DrawDropDown(displayedAssetCount);

            this.position = new Rect(this.position.x, this.position.y, this.position.width, CommandPaletteWindow.kWindowHeight + displayedAssetCount * CommandPaletteWindow.kRowHeight);

            if (CommandPaletteWindow._focusTrigger) {
                CommandPaletteWindow._focusTrigger = false;
                EditorGUI.FocusTextInControl(CommandPaletteWindow.kTextFieldControlName);
            }
        }

        private void HandleInputUpdated() {
            ReparseInput();
            CommandPaletteWindow._selectedIndex = 0;
            CommandPaletteWindow.ReloadObjects();
        }

        private static void ReloadObjects() {
            if (CommandPaletteWindow._commandManager == null) {
                return;
            }

            CommandPaletteWindow._objects = CommandPaletteWindow._commandManager.ObjectsSortedByMatch(CommandPaletteWindow._parsedSearchInput);
        }

        private static void ReparseInput() {
            if (CommandPaletteWindow._input == null || CommandPaletteWindow._input.Length <= 0) {
                CommandPaletteWindow._parsedSearchInput = "";
                CommandPaletteWindow._parsedArguments = null;
                return;
            }

            string[] parameters = CommandPaletteWindow._input.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            CommandPaletteWindow._parsedSearchInput = parameters[0];

            if (parameters.Length == 1) {
                CommandPaletteWindow._parsedArguments = null;
            } else {
                CommandPaletteWindow._parsedArguments = parameters.Skip(1).ToArray();
            }
        }

        private void HandleKeyDownEvent(Event e) {
            switch (e.keyCode) {
                case KeyCode.Escape:
                this.CloseIfNotClosing();
                break;
                case KeyCode.Return:
                ExecuteCommandAtIndex(CommandPaletteWindow._selectedIndex);
                break;
                case KeyCode.DownArrow:
                CommandPaletteWindow._selectedIndex++;
                e.Use();
                break;
                case KeyCode.UpArrow:
                CommandPaletteWindow._selectedIndex--;
                e.Use();
                break;
                default:
                break;
            }
        }

        private void DrawDropDown(int displayedAssetCount) {
            HashSet<char> inputSet = new HashSet<char>();
            foreach (char c in _input.ToLower()) {
                inputSet.Add(c);
            }

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.richText = true;

            GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label);
            subtitleStyle.fontSize = 9;

            int currentIndex = 0;
            for (int i = 0; i < displayedAssetCount; i++) {
                ICommand command = CommandPaletteWindow._objects[i];
                if (!command.IsValid()) {
                    continue;
                }

                float topY = CommandPaletteWindow.kWindowHeight + CommandPaletteWindow.kRowHeight * currentIndex;

                Rect rowRect = new Rect(0.0f, topY, CommandPaletteWindow.kWindowWidth, CommandPaletteWindow.kRowHeight);

                Event e = Event.current;
                if (e.type == EventType.MouseMove) {
                    if (rowRect.Contains(e.mousePosition) && CommandPaletteWindow._selectedIndex != currentIndex) {
                        CommandPaletteWindow._selectedIndex = currentIndex;
                        Repaint();
                    }
                } else if (e.type == EventType.MouseDown && e.button == 0) {
                    if (rowRect.Contains(e.mousePosition)) {
                        ExecuteCommandAtIndex(currentIndex);
                    }
                }

                if (currentIndex == CommandPaletteWindow._selectedIndex) {
                    EditorGUI.DrawRect(rowRect, CommandPaletteWindow._selectedBackgroundColor);
                }

                string title = command.DisplayTitle;
                string subtitle = command.DisplayDetailText;

                int subtitleMaxLength = Math.Min(CommandPaletteWindow.kSubtitleMaxSoftLength + title.Length, CommandPaletteWindow.kSubtitleMaxSoftLength + CommandPaletteWindow.kSubtitleMaxTitleAdditiveLength);
                if (subtitle.Length > subtitleMaxLength + 2) {
                    subtitle = ".." + subtitle.Substring(subtitle.Length - subtitleMaxLength);
                }

                string colorHex = EditorGUIUtility.isProSkin ? "#8e8e8e" : "#383838";

                StringBuilder consecutiveBuilder = new StringBuilder();
                List<string> consecutives = new List<string>();
                bool startedConsecutive = false;
                foreach (char c in title) {
                    if (inputSet.Contains(char.ToLower(c))) {
                        startedConsecutive = true;
                        consecutiveBuilder.Append(c);
                    } else {
                        if (startedConsecutive) {
                            consecutives.Add(consecutiveBuilder.ToString());
                            consecutiveBuilder.Reset();
                            startedConsecutive = false;
                        }
                    }
                }

                // flush whatever is in the string builder
                consecutives.Add(consecutiveBuilder.ToString());

                string maxConsecutive = consecutives.MaxOrDefault(s => s.Length);
                if (!string.IsNullOrEmpty(maxConsecutive)) {
                    title = title.ReplaceFirst(maxConsecutive, string.Format("</color>{0}<color={1}>", maxConsecutive, colorHex));
                    title = string.Format("<color={0}>{1}</color>", colorHex, title);
                }

                if (_debug) {
                    double score = _commandManager.ScoreFor(command, _input);
                    subtitle += string.Format(" (score: {0})", score.ToString("F2"));
                }

                EditorGUI.LabelField(new Rect(0.0f, topY, CommandPaletteWindow.kWindowWidth, CommandPaletteWindow.kRowTitleHeight), title, titleStyle);
                EditorGUI.LabelField(new Rect(0.0f, topY + CommandPaletteWindow.kRowTitleHeight + CommandPaletteWindow.kRowSubtitleHeightPadding, CommandPaletteWindow.kWindowWidth, CommandPaletteWindow.kRowSubtitleHeight), subtitle, subtitleStyle);

                GUIStyle textureStyle = new GUIStyle();
                textureStyle.normal.background = command.DisplayIcon;
                EditorGUI.LabelField(new Rect(CommandPaletteWindow.kWindowWidth - CommandPaletteWindow.kIconEdgeSize - CommandPaletteWindow.kIconPadding, topY + CommandPaletteWindow.kIconPadding, CommandPaletteWindow.kIconEdgeSize, CommandPaletteWindow.kIconEdgeSize), GUIContent.none, textureStyle);

                // NOTE (darren): we only increment currentIndex if we draw the object
                // because it is used for positioning the UI
                currentIndex++;
            }
        }

        private void OnFocus() {
            CommandPaletteWindow._focusTrigger = true;
        }

        private void OnLostFocus() {
            this.CloseIfNotClosing();
        }

        protected void CloseIfNotClosing() {
            if (!CommandPaletteWindow._isClosing) {
                CommandPaletteWindow._isClosing = true;
                this.Close();
            }
        }

        private void ExecuteCommandAtIndex(int index) {
            if (!_objects.ContainsIndex(index)) {
                Debug.LogError("Can't execute command with index because out-of-bounds: " + index);
                return;
            }

            ICommand command = CommandPaletteWindow._objects[index];

            var parsedArguments = CommandPaletteWindow._parsedArguments;
            EditorApplication.delayCall += () => {
                if (parsedArguments != null) {
                    ICommandWithArguments castedObj;
                    try {
                        castedObj = (ICommandWithArguments)command;
                        castedObj.Execute(parsedArguments);
                    } catch (InvalidCastException) {
                        Debug.LogWarning("Attempted to pass arguments to CommandObject, but object does not allow arguments!");
                        command.Execute();
                    }
                } else {
                    command.Execute();
                }
            };

            this.CloseIfNotClosing();
        }
    }
}