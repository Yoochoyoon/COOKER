using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 설정 (docs/19 8번). 효과음·음악 음량과 전체화면을 바꾸고 PlayerPrefs에 저장한다.
// 톱니(설정) 버튼은 모든 화면 오른쪽 위에 떠 있다 (SettingsButton). 누르면 게임을 멈추고 설정 창을 연다. Esc·Enter·돌아가기로 닫는다.
// 음량 값은 Sound.SfxVolume / Sound.MusicVolume에 넣는다.
// 저장 키: cooker.sfx, cooker.bgm, cooker.fullscreen (바꾸면 설정이 초기화된다)
public static class Settings
{
    const string KeySfx = "cooker.sfx", KeyBgm = "cooker.bgm", KeyFull = "cooker.fullscreen";
    const int Step = 10;

    public static int SfxPercent = 100;
    public static int MusicPercent = 50;
    public static bool Fullscreen = true;
    public static bool IsOpen; // 설정 창이 열려 있으면 뒤의 게임(GameFlow.Update)은 입력을 받지 않는다

    static Text sfxTxt, bgmTxt, fullTxt;

    // 게임 시작 때 한 번 읽는다 (GameFlow.Awake). 읽은 값은 Sound와 화면 모드에 바로 적용된다.
    public static void Load()
    {
        try
        {
            SfxPercent = Mathf.Clamp(PlayerPrefs.GetInt(KeySfx, SfxPercent), 0, 100);
            MusicPercent = Mathf.Clamp(PlayerPrefs.GetInt(KeyBgm, MusicPercent), 0, 100);
            Fullscreen = PlayerPrefs.GetInt(KeyFull, Screen.fullScreen ? 1 : 0) == 1;
        }
        catch (System.Exception) { /* 저장소를 못 읽으면 기본값으로 연다 */ }
        Apply();
    }

    static void Save()
    {
        try
        {
            PlayerPrefs.SetInt(KeySfx, SfxPercent);
            PlayerPrefs.SetInt(KeyBgm, MusicPercent);
            PlayerPrefs.SetInt(KeyFull, Fullscreen ? 1 : 0);
            PlayerPrefs.Save();
        }
        catch (System.Exception) { /* 저장이 안 되어도 게임은 계속 돈다 */ }
    }

    static void Apply()
    {
        Sound.SfxVolume = SfxPercent / 100f;
        Sound.MusicVolume = MusicPercent / 100f;
        // 지금 나오는 음악에도 바로 적용한다. Sound.cs는 곡이 바뀔 때만 음량을 맞추므로, 여기서 음악 재생기를 직접 찾는다.
        // (Sound.cs가 "Sound" 오브젝트에 효과음 AudioSource, 음악 AudioSource 순서로 만든다는 점에 기대고 있다. 나중에 Sound.ApplyVolume()으로 바꾸면 된다.)
        var soundGo = GameObject.Find("Sound");
        if (soundGo != null)
        {
            var srcs = soundGo.GetComponents<AudioSource>();
            if (srcs.Length > 1) srcs[1].volume = Sound.MusicVolume;
        }
        Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }

    // 설정 패널(부모 안을 꽉 채운다)의 내용을 그린다. close는 "돌아가기"를 눌렀을 때 실행한다.
    public static void BuildPanel(RectTransform panel, System.Action close)
    {
        UIKit.Label(panel, "설정", 0.1f, 0.78f, 0.9f, 0.92f, 90, TextAnchor.MiddleCenter, UIKit.Accent);

        UIKit.Label(panel, "효과음", 0.12f, 0.60f, 0.48f, 0.72f, 48, TextAnchor.MiddleLeft);
        sfxTxt = VolumeRow(panel, 0.60f, 0.72f, () => SfxPercent, v => SfxPercent = v);

        UIKit.Label(panel, "음악", 0.12f, 0.44f, 0.48f, 0.56f, 48, TextAnchor.MiddleLeft);
        bgmTxt = VolumeRow(panel, 0.44f, 0.56f, () => MusicPercent, v => MusicPercent = v);

        UIKit.Label(panel, "전체화면", 0.12f, 0.28f, 0.48f, 0.40f, 48, TextAnchor.MiddleLeft);
        fullTxt = UIKit.Btn(panel, "", 0.55f, 0.28f, 0.89f, 0.40f, () =>
        {
            Fullscreen = !Fullscreen;
            Apply();
            Save();
            Refresh();
        }, null, 44).GetComponentInChildren<Text>();

        UIKit.PrimaryBtn(panel, "돌아가기 [Esc]", 0.30f, 0.07f, 0.70f, 0.19f, close, 44);
        Refresh();
    }

    // − 값 + 한 줄을 만든다. 값 글자(Text)를 돌려준다.
    static Text VolumeRow(RectTransform root, float y0, float y1, System.Func<int> get, System.Action<int> set)
    {
        UIKit.Btn(root, "−", 0.55f, y0, 0.63f, y1, () => { Change(get, set, -Step); }, UIKit.Muted, 44);
        var val = UIKit.Label(root, "", 0.64f, y0, 0.80f, y1, 44, TextAnchor.MiddleCenter);
        UIKit.Btn(root, "+", 0.81f, y0, 0.89f, y1, () => { Change(get, set, Step); }, UIKit.Accent, 44);
        return val;
    }

    static void Change(System.Func<int> get, System.Action<int> set, int delta)
    {
        set(Mathf.Clamp(get() + delta, 0, 100));
        Apply();
        Save();
        Refresh();
    }

    static void Refresh()
    {
        if (sfxTxt != null) sfxTxt.text = SfxPercent + "%";
        if (bgmTxt != null) bgmTxt.text = MusicPercent + "%";
        if (fullTxt != null) fullTxt.text = Fullscreen ? "켜짐" : "꺼짐";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Object.FindFirstObjectByType<SettingsButton>() != null) return;
        var go = new GameObject("COOKER_Settings");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<SettingsButton>();
    }
}

// 모든 화면 위에 떠 있는 톱니 버튼과 설정 창. 게임 화면과 따로 캔버스를 가진다.
public class SettingsButton : MonoBehaviour
{
    RectTransform canvasRT;
    GameObject dim;

    void Awake()
    {
        var cgo = new GameObject("SettingsCanvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4000; // 게임 화면과 장식 위, 도움말(5000) 아래
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        canvasRT = (RectTransform)cgo.transform;

        // 위쪽 줄: 도움말 옆 (x 770~840, y 30~100 픽셀). 도움말·설정·나가기가 HUD와 한 줄에 놓인다
        UIKit.ImageBtn(canvasRT, "icon_gear", 0.4010f, 0.9074f, 0.4375f, 0.9722f, Open);
    }

    void Update()
    {
        if (!Settings.IsOpen) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Close();
    }

    void Open()
    {
        if (Settings.IsOpen) return;
        Settings.IsOpen = true;
        Time.timeScale = 0f; // 조리 타이머와 젓기·뚜껑 같은 시간 흐름을 멈춘다
        dim = UIKit.Box(canvasRT, "SettingsDim", 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.55f), false).gameObject; // 뒤쪽 클릭을 막는다
        var panel = UIKit.Box(dim.transform, "SettingsPanel", 0.2f, 0.16f, 0.8f, 0.84f, UIKit.Card);
        Settings.BuildPanel(panel, Close);
    }

    void Close()
    {
        if (!Settings.IsOpen) return;
        Settings.IsOpen = false;
        Time.timeScale = 1f;
        if (dim != null) Destroy(dim);
        dim = null;
    }
}
