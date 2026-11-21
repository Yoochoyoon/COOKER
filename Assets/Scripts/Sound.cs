using System.Collections.Generic;
using UnityEngine;

// 효과음과 배경 음악 재생기. 파일이 없으면 조용히 넘어가서 소리 파일 없이도 게임이 돈다.
// 규칙: Assets/Resources/Sfx/<이름>.wav (효과음), Assets/Resources/Sfx/<이름>.ogg (배경 음악)
// 이름은 docs/19의 효과음 표를 따른다: put, take, flip, stir, lid, boil_over, success, fail, click
public static class Sound
{
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    static readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
    static AudioSource sfx, music;

    public static float SfxVolume = 1f;   // 설정 화면(Settings.cs)에서 바꾼다
    public static float MusicVolume = 0.5f;

    // 도메인 리로드를 끄고 Play하면 static이 남는다. 이전 실행의 "없음" 기억과 사라진 AudioSource를 비운다 (Art.cs와 같은 이유)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { cache.Clear(); lastPlayed.Clear(); sfx = null; music = null; }

    static AudioClip Clip(string name)
    {
        AudioClip c;
        if (cache.TryGetValue(name, out c)) return c;
        c = Resources.Load<AudioClip>("Sfx/" + name);
        cache[name] = c; // 없는 파일(null)도 기억해서 매번 찾지 않는다
        return c;
    }

    static void Ensure()
    {
        if (sfx != null) return;
        var go = new GameObject("Sound");
        Object.DontDestroyOnLoad(go);
        sfx = go.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        music = go.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
    }

    public static void Play(string name) { Play(name, 0f); }

    // minGap: 같은 소리를 이 시간(초) 안에 다시 틀지 않는다. 젓기 연타처럼 매우 잦은 소리가 겹쳐 시끄러워지지 않게.
    public static void Play(string name, float minGap)
    {
        var c = Clip(name);
        if (c == null) return;
        if (minGap > 0f)
        {
            float last;
            if (lastPlayed.TryGetValue(name, out last) && Time.unscaledTime - last < minGap) return;
            lastPlayed[name] = Time.unscaledTime;
        }
        Ensure();
        sfx.PlayOneShot(c, SfxVolume);
    }

    // 배경 음악은 루프로 튼다. 같은 곡이 이미 나오고 있으면 처음부터 다시 틀지 않는다.
    public static void Music(string name)
    {
        var c = Clip(name);
        if (c == null) { StopMusic(); return; } // 곡 파일이 없는 화면에서 이전 화면 곡이 계속 나오지 않게
        Ensure();
        music.volume = MusicVolume;
        if (music.clip == c && music.isPlaying) return;
        music.clip = c;
        music.Play();
    }

    public static void StopMusic()
    {
        if (music != null) music.Stop();
    }
}
