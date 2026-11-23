using UnityEngine;
using UnityEngine.UI;

// 설정 화면 (docs/19 8번). 효과음·음악 음량과 전체화면을 바꾸고 PlayerPrefs에 저장한다.
// 화면은 GameFlow.Core의 ShowSettings가 Open()으로 그린다. 음량 값은 Sound.SfxVolume / Sound.MusicVolume에 넣는다.
// 저장 키: cooker.sfx, cooker.bgm, cooker.fullscreen (바꾸면 설정이 초기화된다)
public static class Settings
{
    const string KeySfx = "cooker.sfx", KeyBgm = "cooker.bgm", KeyFull = "cooker.fullscreen";
    const int Step = 10;

    public static int SfxPercent = 100;
    public static int MusicPercent = 50;
    public static bool Fullscreen = true;

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
        Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }

    // root 안에 설정 화면을 그린다. back은 "돌아가기"를 눌렀을 때 실행한다.
    public static void Open(RectTransform root, System.Action back)
    {
        UIKit.Label(root, "설정", 0.1f, 0.78f, 0.9f, 0.92f, 90, TextAnchor.MiddleCenter, UIKit.Accent);

        UIKit.Label(root, "효과음", 0.12f, 0.60f, 0.48f, 0.72f, 48, TextAnchor.MiddleLeft);
        sfxTxt = VolumeRow(root, 0.60f, 0.72f, () => SfxPercent, v => SfxPercent = v);

        UIKit.Label(root, "음악", 0.12f, 0.44f, 0.48f, 0.56f, 48, TextAnchor.MiddleLeft);
        bgmTxt = VolumeRow(root, 0.44f, 0.56f, () => MusicPercent, v => MusicPercent = v);

        UIKit.Label(root, "전체화면", 0.12f, 0.28f, 0.48f, 0.40f, 48, TextAnchor.MiddleLeft);
        fullTxt = UIKit.Btn(root, "", 0.55f, 0.28f, 0.89f, 0.40f, () =>
        {
            Fullscreen = !Fullscreen;
            Apply();
            Save();
            Refresh();
        }, null, 44).GetComponentInChildren<Text>();

        UIKit.PrimaryBtn(root, "돌아가기 [Enter]", 0.30f, 0.07f, 0.70f, 0.19f, back, 44);
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
}
