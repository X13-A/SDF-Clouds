using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class PostProcessBase : MonoBehaviour
{
    public abstract void Apply(RenderTexture source, RenderTexture dest, Camera camera);
}
