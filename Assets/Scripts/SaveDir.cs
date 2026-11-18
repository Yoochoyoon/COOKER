using System.IO;
using UnityEngine;

// 플레이 기록(도감·칭호·요리 이름)을 저장하는 폴더.
// 유니티 에디터에서 테스트한 기록이 빌드 게임에 섞이지 않도록, 에디터는 하위 폴더 EditorTest에 따로 저장한다.
// 요리 사진은 기록이 아니라 미리 만들어 프로젝트에 넣어 둔 파일(Resources/AiDishes)이라 여기에 저장하지 않는다.
public static class SaveDir
{
    public static string Path
    {
        get
        {
            string dir = Application.isEditor
                ? System.IO.Path.Combine(Application.persistentDataPath, "EditorTest")
                : Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string File(string name) { return System.IO.Path.Combine(Path, name); }
}
