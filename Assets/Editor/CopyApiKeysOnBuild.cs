using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 빌드가 끝나면 API 키 파일(UserSettings/CookerApiKeys.txt)을 빌드 폴더의 UserSettings/에 복사한다.
// 빌드된 게임은 실행 파일 옆 UserSettings/CookerApiKeys.txt를 읽으므로(DishPhoto.Setting), 이것으로 명전 챗봇(BAZE) 키로
// 고화질 요리 사진이 바로 나온다. 키는 깃에 올라가지 않는다(UserSettings는 무시 목록).
// 주의: 빌드 폴더를 남에게 넘기면 키도 같이 넘어간다. 공유용 빌드라면 그 폴더의 UserSettings를 지우고 넘긴다.
public class CopyApiKeysOnBuild : IPostprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }

    public void OnPostprocessBuild(BuildReport report)
    {
        string src = Path.Combine(Path.GetDirectoryName(Application.dataPath), "UserSettings", "CookerApiKeys.txt");
        if (!File.Exists(src))
        {
            Debug.LogWarning("API 키 파일이 없어 빌드에 넣지 못했다: " + src + " (AI 요리 사진은 무료 서비스로 대체된다)");
            return;
        }
        string outPath = report.summary.outputPath;                       // Windows: ...\COOKER.exe
        string buildDir = Directory.Exists(outPath) ? outPath : Path.GetDirectoryName(outPath);
        string dstDir = Path.Combine(buildDir, "UserSettings");
        Directory.CreateDirectory(dstDir);
        File.Copy(src, Path.Combine(dstDir, "CookerApiKeys.txt"), true);
        Debug.Log("API 키 파일을 빌드 폴더에 복사했다: " + dstDir);
    }
}
