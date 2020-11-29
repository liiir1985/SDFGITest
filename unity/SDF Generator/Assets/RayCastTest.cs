using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
public class RayCastTest : MonoBehaviour,IPointerClickHandler
{
    public SDFGenerator.SDFGI gi;
    public void OnPointerClick(PointerEventData eventData)
    {
        var ep = new Vector2(574.4f, 112.5f);
        var pos = ep / 2f;// eventData.position / 2;
        pos.y = 309 - pos.y;
        int index = (int)pos.y * 604 + (int)pos.x;
        gi.PathTrace(index);
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
