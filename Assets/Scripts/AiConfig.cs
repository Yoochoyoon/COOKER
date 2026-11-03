using System.IO;
using UnityEngine;

// AI 설정. API 키는 코드나 레포에 넣지 않고 프로젝트 폴더의 ai.local.json에서 읽는다(.gitignore 대상).
// 빌드한 경우에는 exe 파일과 같은 폴더의 ai.local.json을 읽는다.
// 파일이 없거나 키가 비어 있으면 AI 기능이 꺼지고, 게임은 규칙 이름으로 그대로 동작한다.
[System.Serializable]
public class AiSettings
{
    public string anthropicKey = "";
    public string anthropicModel = "claude-haiku-5-5";
    public string imageProvider = "none"; // 이미지 모델이 정해지면 AiDishImage.Provider()에 추가한 이름을 적는다
    public string imageKey = "";
    public string imageModel = "";
}

public static class AiConfig
{
    public const int NameTimeout = 5;    // 이름·설명 요청 제한(초)
    public const int ImageTimeout = 25;  // 그림 요청 제한(초)

    static AiSettings settings;

    public static string FilePath
    {
        get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../ai.local.json")); }
    }

    public static AiSettings Get()
    {
        if (settings != null) return settings;
        settings = new AiSettings();
        try
        {
            if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), settings);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[AI] 설정 파일을 읽지 못했다: " + e.Message);
        }
        return settings;
    }

    public static bool NameReady { get { return !string.IsNullOrEmpty(Get().anthropicKey); } }
}
