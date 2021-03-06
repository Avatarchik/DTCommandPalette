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

			InitializeWindow("Open.. ", commandManager);
		}

		[MenuItem("Window/Open Command Palette.. %#m")]
		public static void ShowCommandPaletteWindow() {
			var commandManager = new CommandManager();
			commandManager.AddLoader(new MethodCommandLoader());

			InitializeWindow("Command Palette.. ", commandManager);
		}

		public static void InitializeWindow(string title, CommandManager commandManager, bool clearInput = false) {
			if (commandManager == null) {
				Debug.LogError("CommandPaletteWindow: Can't initialize a window without a command manager!");
				return;
			}

			if (clearInput) {
				input_ = "";
			}

			EditorWindow window = EditorWindow.GetWindow(typeof(CommandPaletteWindow), utility: true, title: title, focus: true);
			window.position = new Rect(0.0f, 0.0f, kWindowWidth, kWindowHeight);
			window.CenterInMainEditorWindow();
			window.wantsMouseMove = true;

			selectedIndex_ = 0;
			focusTrigger_ = true;
			isClosing_ = false;

			commandManager_ = commandManager;
			ReloadObjects();
		}

		// PRAGMA MARK - Internal
		protected static string input_ = "";
		protected static bool focusTrigger_ = false;
		protected static bool isClosing_ = false;
		protected static int selectedIndex_ = 0;
		protected static ICommand[] objects_ = new ICommand[0];
		protected static CommandManager commandManager_ = null;
		protected static Color selectedBackgroundColor_ = ColorUtil.HexStringToColor("#4076d3").WithAlpha(0.4f);

		private static string parsedSearchInput_ = "";
		private static string[] parsedArguments_ = null;

		protected void OnGUI() {
			Event e = Event.current;
			switch (e.type) {
				case EventType.KeyDown:
					HandleKeyDownEvent(e);
					break;
				default:
					break;
			}

			if (objects_.Length > 0) {
				selectedIndex_ = MathUtil.Wrap(selectedIndex_, 0, Mathf.Min(objects_.Length, kMaxRowsDisplayed));
			} else {
				selectedIndex_ = 0;
			}

			GUIStyle textFieldStyle = new GUIStyle(GUI.skin.textField);
			textFieldStyle.fontSize = kFontSize;

			GUI.SetNextControlName(kTextFieldControlName);
			string updatedInput = EditorGUI.TextField(new Rect(0.0f, 0.0f, kWindowWidth, kWindowHeight), input_, textFieldStyle);
			if (updatedInput != input_) {
				input_ = updatedInput;
				HandleInputUpdated();
			}

			int displayedAssetCount = Mathf.Min(objects_.Length, kMaxRowsDisplayed);
			DrawDropDown(displayedAssetCount);

			this.position = new Rect(this.position.x, this.position.y, this.position.width, kWindowHeight + displayedAssetCount * kRowHeight);

			if (focusTrigger_) {
				focusTrigger_ = false;
				EditorGUI.FocusTextInControl(kTextFieldControlName);
			}
		}

		private void HandleInputUpdated() {
			ReparseInput();
			selectedIndex_ = 0;
			ReloadObjects();
		}

		private static void ReloadObjects() {
			if (commandManager_ == null) {
				return;
			}

			objects_ = commandManager_.ObjectsSortedByMatch(parsedSearchInput_);
		}

		private static void ReparseInput() {
			if (input_ == null || input_.Length <= 0) {
				parsedSearchInput_ = "";
				parsedArguments_ = null;
				return;
			}

			string[] parameters = input_.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

			parsedSearchInput_ = parameters[0];

			if (parameters.Length == 1) {
				parsedArguments_ = null;
			} else {
				parsedArguments_ = parameters.Skip(1).ToArray();
			}
		}

		private void HandleKeyDownEvent(Event e) {
			switch (e.keyCode) {
				case KeyCode.Escape:
					CloseIfNotClosing();
					break;
				case KeyCode.Return:
					ExecuteCommandAtIndex(selectedIndex_);
					break;
				case KeyCode.DownArrow:
					selectedIndex_++;
					e.Use();
					break;
				case KeyCode.UpArrow:
					selectedIndex_--;
					e.Use();
					break;
				default:
					break;
			}
		}

		private void DrawDropDown(int displayedAssetCount) {
			HashSet<char> inputSet = new HashSet<char>();
			foreach (char c in input_.ToLower()) {
				inputSet.Add(c);
			}

			GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
			titleStyle.fontStyle = FontStyle.Bold;
			titleStyle.richText = true;

			GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label);
			subtitleStyle.fontSize = 9;

			int currentIndex = 0;
			for (int i = 0; i < displayedAssetCount; i++) {
				ICommand command = objects_[i];
				if (!command.IsValid()) {
					continue;
				}

				float topY = kWindowHeight + kRowHeight * currentIndex;

				Rect rowRect = new Rect(0.0f, topY, kWindowWidth, kRowHeight);

				Event e = Event.current;
				if (e.type == EventType.MouseMove) {
					if (rowRect.Contains(e.mousePosition) && selectedIndex_ != currentIndex) {
						selectedIndex_ = currentIndex;
						Repaint();
					}
				} else if (e.type == EventType.MouseDown && e.button == 0) {
					if (rowRect.Contains(e.mousePosition)) {
						ExecuteCommandAtIndex(currentIndex);
					}
				}

				if (currentIndex == selectedIndex_) {
					EditorGUI.DrawRect(rowRect, selectedBackgroundColor_);
				}

				string title = command.DisplayTitle;
				string subtitle = command.DisplayDetailText;

				int subtitleMaxLength = Math.Min(kSubtitleMaxSoftLength + title.Length, kSubtitleMaxSoftLength + kSubtitleMaxTitleAdditiveLength);
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
				}
				title = string.Format("<color={0}>{1}</color>", colorHex, title);

				if (_debug) {
					double score = commandManager_.ScoreFor(command, input_);
					subtitle += string.Format(" (score: {0})", score.ToString("F2"));
				}

				EditorGUI.LabelField(new Rect(0.0f, topY, kWindowWidth, kRowTitleHeight), title, titleStyle);
				EditorGUI.LabelField(new Rect(0.0f, topY + kRowTitleHeight + kRowSubtitleHeightPadding, kWindowWidth, kRowSubtitleHeight), subtitle, subtitleStyle);

				GUIStyle textureStyle = new GUIStyle();
				textureStyle.normal.background = command.DisplayIcon;
				EditorGUI.LabelField(new Rect(kWindowWidth - kIconEdgeSize - kIconPadding, topY + kIconPadding, kIconEdgeSize, kIconEdgeSize), GUIContent.none, textureStyle);

				// NOTE (darren): we only increment currentIndex if we draw the object
				// because it is used for positioning the UI
				currentIndex++;
			}
		}

		private void OnFocus() {
			focusTrigger_ = true;
		}

		private void OnLostFocus() {
			CloseIfNotClosing();
		}

		protected void CloseIfNotClosing() {
			if (!isClosing_) {
				isClosing_ = true;
				Close();
			}
		}

		private void ExecuteCommandAtIndex(int index) {
			if (!objects_.ContainsIndex(index)) {
				Debug.LogError("Can't execute command with index because out-of-bounds: " + index);
				return;
			}

			ICommand command = objects_[index];

			var parsedArguments = parsedArguments_;
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

			CloseIfNotClosing();
		}
	}
}