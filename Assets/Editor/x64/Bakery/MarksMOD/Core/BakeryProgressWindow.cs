#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class BakeryProgressWindow : EditorWindow
{
    static BakeryProgressWindow instance;

    public static void ShowOrUpdate()
    {
        if (Application.isBatchMode) return;
        if (instance == null)
        {
            instance = GetWindow<BakeryProgressWindow>(false, "Bakery Progress", true);
            instance.minSize = new Vector2(420, 96);
            instance.maxSize = new Vector2(900, 96);
            instance.Show();
        }
        instance.Repaint();
    }

    public static void Hide()
    {
        if (instance == null) return;
        instance.Close();
        instance = null;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField(BakeryProgress.Header, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(BakeryProgress.Message);
        Rect rect = GUILayoutUtility.GetRect(18, 18, "TextField");
        EditorGUI.ProgressBar(rect, BakeryProgress.Percent, Mathf.RoundToInt(BakeryProgress.Percent * 100f) + "%");
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(BakeryProgress.CancelRequested))
        {
            if (GUILayout.Button("Cancel")) BakeryProgress.RequestCancel();
        }
    }
}
#endif
