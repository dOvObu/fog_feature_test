using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class DebugUI : MonoBehaviour
{
    [SerializeField] Slider _slider;
    [SerializeField] TMP_Text _text;
    
    void OnEnable()
    {
        _slider.onValueChanged.AddListener(OnScale);
    }

    void OnDestroy()
    {
        _slider.onValueChanged.RemoveAllListeners();
    }

    void OnScale(float scale)
    {
        VolumeManager.instance.stack.GetComponent<HDFogVolume>().scaleFactor.value = (int)scale;
        _text.text = $"{(int)scale}";
    }
}
