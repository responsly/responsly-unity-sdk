using System;
using System.Collections.Generic;
using Responsly;
using UnityEngine;

/// <summary>
/// A test panel for the Responsly SDK. Add this component to an empty GameObject in an empty
/// scene, enter your SDK key and a Survey ID (in the Inspector or on the device) and build to an
/// iOS or Android device, simulator or emulator. In the Editor the buttons only write log lines:
/// the SDK does nothing there.
///
/// The panel is drawn with IMGUI, so it needs no assets. Every SDK event is written to the log at
/// the bottom, and the game clock stops while a survey is on screen.
/// </summary>
public class ResponslyDemo : MonoBehaviour
{
    [Header("Initialize")]
    [SerializeField] private string sdkKey = "";
    [SerializeField] private string apiHost = ResponslyOptions.DefaultApiHost;

    [Header("Calls")]
    [SerializeField] private string userId = "user-123";
    [SerializeField] private string eventName = "level_completed";
    [SerializeField] private string screenName = "MainMenu";
    [SerializeField] private string surveyId = "";
    [SerializeField] private bool force = true;

    private const int MaxLogLines = 200;

    private readonly List<string> log = new List<string>();
    private readonly List<string> pendingLog = new List<string>();
    private bool clearLog;
    private Vector2 logScroll;
    private GUIStyle logStyle;
    private bool targetingPaused;
    private bool guardVetoes;
    private bool surveyOnScreen;
    private float timeScaleBeforeSurvey = 1f;

    private void OnEnable()
    {
        ResponslySDK.SurveyShown += OnSurveyShown;
        ResponslySDK.SurveyNotShown += OnSurveyNotShown;
        ResponslySDK.QuestionAnswered += OnQuestionAnswered;
        ResponslySDK.SurveyCompleted += OnSurveyCompleted;
        ResponslySDK.SurveyDismissed += OnSurveyDismissed;
        ResponslySDK.SurveyError += OnSurveyError;
    }

    private void OnDisable()
    {
        ResponslySDK.SurveyShown -= OnSurveyShown;
        ResponslySDK.SurveyNotShown -= OnSurveyNotShown;
        ResponslySDK.QuestionAnswered -= OnQuestionAnswered;
        ResponslySDK.SurveyCompleted -= OnSurveyCompleted;
        ResponslySDK.SurveyDismissed -= OnSurveyDismissed;
        ResponslySDK.SurveyError -= OnSurveyError;
        ResumeGame();
    }

    // ---------------------------------------------------------------------------------------------
    // Events. They arrive on Unity's main thread.
    // ---------------------------------------------------------------------------------------------

    private void OnSurveyShown(SurveyShownEventArgs e)
    {
        PauseGame();
        Log("SurveyShown  " + e.Survey.Id + " \"" + e.Survey.Name + "\"  trigger: " + e.Trigger.Type + " " + e.Trigger.Name);
    }

    private void OnSurveyNotShown(SurveyNotShownEventArgs e)
    {
        Log("SurveyNotShown  " + e.SurveyId + "  reason: " + e.Reason);
    }

    private void OnQuestionAnswered(QuestionAnsweredEventArgs e)
    {
        Log("QuestionAnswered  " + e.QuestionId + " (" + e.Type + ") \"" + e.Title + "\"  value: " + e.ValueJson);
    }

    private void OnSurveyCompleted(SurveyCompletedEventArgs e)
    {
        ResumeGame();
        Log("SurveyCompleted  " + e.SurveyId + "  response: " + e.ResponseId + "  answers: " + e.Answers.Count);
        for (int index = 0; index < e.Answers.Count; index++)
        {
            ResponslyAnswer answer = e.Answers[index];
            Log("    " + answer.QuestionId + " (" + answer.Type + ") = " + answer.ValueJson);
        }
    }

    private void OnSurveyDismissed(SurveyDismissedEventArgs e)
    {
        ResumeGame();
        Log("SurveyDismissed  " + e.SurveyId);
    }

    private void OnSurveyError(SurveyErrorEventArgs e)
    {
        Log("SurveyError  " + e.SurveyId + "  " + e.Code + ": " + e.Message);
    }

    // A survey is drawn above the game; the game does not stop by itself.
    private void PauseGame()
    {
        if (!surveyOnScreen)
        {
            surveyOnScreen = true;
            timeScaleBeforeSurvey = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }
    }

    private void ResumeGame()
    {
        if (surveyOnScreen)
        {
            surveyOnScreen = false;
            Time.timeScale = timeScaleBeforeSurvey;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Panel
    // ---------------------------------------------------------------------------------------------

    private void OnGUI()
    {
        // IMGUI needs the same controls in the layout pass and in the pass that follows it, so the
        // log only changes here, at the start of a layout pass.
        if (Event.current.type == EventType.Layout)
        {
            if (clearLog)
            {
                clearLog = false;
                log.Clear();
            }
            if (pendingLog.Count > 0)
            {
                log.AddRange(pendingLog);
                pendingLog.Clear();
                if (log.Count > MaxLogLines)
                {
                    log.RemoveRange(0, log.Count - MaxLogLines);
                }
            }
        }

        // About 400 units across the short side of the screen, whatever its resolution.
        float scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 400f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        // Screen.safeArea starts at the bottom left, IMGUI at the top left.
        Rect safe = Screen.safeArea;
        Rect area = new Rect(
            safe.xMin / scale + 8f,
            (Screen.height - safe.yMax) / scale + 8f,
            safe.width / scale - 16f,
            safe.height / scale - 16f);

        if (logStyle == null)
        {
            logStyle = new GUIStyle(GUI.skin.label);
            logStyle.wordWrap = true;
        }
        GUILayoutOption tall = GUILayout.Height(34f);
        GUILayoutOption labelWidth = GUILayout.Width(70f);
        GUILayoutOption buttonWidth = GUILayout.Width(110f);

        GUILayout.BeginArea(area);

        GUILayout.Label("Responsly SDK " + ResponslySDK.SdkVersion + " demo    game time: " + Time.time.ToString("0.0")
            + (surveyOnScreen ? "  (stopped: survey on screen)" : ""));

        GUILayout.BeginHorizontal();
        GUILayout.Label("SDK key", labelWidth, tall);
        sdkKey = GUILayout.TextField(sdkKey, tall);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("API host", labelWidth, tall);
        apiHost = GUILayout.TextField(apiHost, tall);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Initialize", tall))
        {
            ResponslySDK.Initialize(sdkKey.Trim(), new ResponslyOptions
            {
                ApiHost = apiHost.Trim(),
                LogLevel = ResponslyLogLevel.Debug,
            });
            Log("Initialize");
        }

        GUILayout.BeginHorizontal();
        userId = GUILayout.TextField(userId, tall);
        if (GUILayout.Button("Identify", buttonWidth, tall))
        {
            ResponslySDK.Identify(userId.Trim(), new Dictionary<string, object>
            {
                { "plan", "pro" },
                { "level", 12 },
                { "beta", true },
                { "signed_up", DateTime.UtcNow },
            });
            Log("Identify " + userId);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        eventName = GUILayout.TextField(eventName, tall);
        if (GUILayout.Button("Track", buttonWidth, tall))
        {
            ResponslySDK.Track(eventName.Trim(), new Dictionary<string, object> { { "level", 12 }, { "stars", 3 } });
            Log("Track " + eventName);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        screenName = GUILayout.TextField(screenName, tall);
        if (GUILayout.Button("Screen", buttonWidth, tall))
        {
            ResponslySDK.Screen(screenName.Trim());
            Log("Screen " + screenName);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        surveyId = GUILayout.TextField(surveyId, tall);
        force = GUILayout.Toggle(force, "Force", GUILayout.Width(60f), tall);
        if (GUILayout.Button("Show survey", buttonWidth, tall))
        {
            ResponslySDK.ShowSurvey(surveyId.Trim(), new ShowOptions
            {
                Force = force,
                HiddenFields = new Dictionary<string, string> { { "source", "unity-demo" } },
            });
            Log("ShowSurvey " + surveyId + (force ? " (force)" : ""));
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(targetingPaused ? "Resume" : "Pause", tall))
        {
            targetingPaused = !targetingPaused;
            if (targetingPaused)
            {
                ResponslySDK.Pause();
            }
            else
            {
                ResponslySDK.Resume();
            }
            Log(targetingPaused ? "Pause" : "Resume");
        }
        if (GUILayout.Button("Close", tall))
        {
            ResponslySDK.CloseSurvey();
            Log("CloseSurvey");
        }
        if (GUILayout.Button("Reset", tall))
        {
            ResponslySDK.Reset();
            Log("Reset");
        }
        GUILayout.EndHorizontal();

        bool veto = GUILayout.Toggle(guardVetoes, "Display guard vetoes surveys (not asked with Force)", tall);
        if (veto != guardVetoes)
        {
            guardVetoes = veto;
            if (guardVetoes)
            {
                ResponslySDK.SetDisplayGuard(survey => false);
            }
            else
            {
                ResponslySDK.SetDisplayGuard(null);
            }
            Log(guardVetoes ? "SetDisplayGuard(survey => false)" : "SetDisplayGuard(null)");
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Log");
        if (GUILayout.Button("Clear", GUILayout.Width(70f)))
        {
            pendingLog.Clear();
            clearLog = true;
        }
        GUILayout.EndHorizontal();

        logScroll = GUILayout.BeginScrollView(logScroll, GUI.skin.box);
        for (int index = log.Count - 1; index >= 0; index--)
        {
            GUILayout.Label(log[index], logStyle);
        }
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    // Newest line first on screen; also in the device log.
    private void Log(string line)
    {
        pendingLog.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
        Debug.Log("[ResponslyDemo] " + line);
    }
}
