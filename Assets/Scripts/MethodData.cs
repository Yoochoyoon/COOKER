using UnityEngine;

// 조리 방식 한 가지의 수치와 안내 문구를 담는 에셋. 인스펙터에서 바로 고친다.
// 파일 위치: Assets/Resources/Data/Methods/ (방식 하나에 에셋 하나)
[CreateAssetMenu(menuName = "COOKER/Method", fileName = "Method")]
public class MethodData : ScriptableObject
{
    public Method id;            // 방식 종류. 저장과 사진 키에 쓰이므로 바꾸지 않는다
    public string methodName;    // 굽기
    public string verb;          // 구운
    public float speed;          // 중불 기준 초당 익힘
    public float lo, hi;         // 적정 구간
    [TextArea] public string guide;

    public MethodDef ToMethodDef()
    {
        return new MethodDef { id = id, name = methodName, verb = verb, speed = speed, lo = lo, hi = hi, guide = guide };
    }
}
