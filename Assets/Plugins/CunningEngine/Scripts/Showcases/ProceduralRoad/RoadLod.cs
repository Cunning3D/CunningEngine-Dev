#if UNITY_EDITOR
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
public class RoadLod : MonoBehaviour
{
    private Dictionary<GameObject, Vector3> ObjToPos = new Dictionary<GameObject, Vector3>();
    private const int BatchSize = 1000; // 每批处理的最大元素数量
    void Start()
    {
        var childrens = transform.GetComponentsInChildren<BoxCollider>();
        foreach (var item in childrens)
        {
            ObjToPos.Add(item.gameObject, item.center);
        }
        Debug.Log(ObjToPos.Count);
        InvokeRepeating("ButtonClick", 0f, 2f);
    }

    void Update()
    {

    }

    [Button("刷新")]
    public void ButtonClick()
    {
        StartCoroutine(ShowOrHide());
    }

    IEnumerator ShowOrHide()
    {
        int currentCount = 0;
        Dictionary<GameObject, bool> temp = new Dictionary<GameObject, bool>();
        int count = 0;

        foreach (var item in ObjToPos)
        {
            bool visible = IsVisibleToCamera(UnityEditor.SceneView.lastActiveSceneView.camera, item.Value);
            if (visible != item.Key.active)
            {
                temp.Add(item.Key, visible);
            }

            currentCount++;
            if (currentCount >= BatchSize)
            {
                foreach (var obj in temp)
                {
                    obj.Key.SetActive(obj.Value);
                    count++;
                }
                temp.Clear();
                currentCount = 0;
                yield return null;
            }

        }
        foreach (var obj in temp)
        {
            obj.Key.SetActive(obj.Value);
        }
        Debug.Log("结束");
        Debug.Log(count);
    }

    bool IsVisibleToCamera(Camera camera, Vector3 point)
    {
        Vector3 viewportPoint = camera.WorldToViewportPoint(point);
        return viewportPoint.z > 0 && viewportPoint.z < 1000 && viewportPoint.x >= 0 && viewportPoint.x <= 1 && viewportPoint.y >= 0 && viewportPoint.y <= 1;
    }
}
#endif