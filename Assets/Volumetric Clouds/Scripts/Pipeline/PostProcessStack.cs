using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode, ImageEffectAllowedInSceneView]
public class PostProcessStack : MonoBehaviour
{
    [SerializeField] private List<PostProcessBase> processings = new List<PostProcessBase>();

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        //// TODO: remove this debug section and support VR
        //processings[0].Apply(source, destination);
        //return;
        if (processings.Count == 0)
        {
            Graphics.Blit(source, destination);
            return;
        }

        RenderTexture currentSource = source;
        RenderTexture temp = RenderTexture.GetTemporary(source.width, source.height);

        for (int i = 0; i < processings.Count; i++)
        {
            if (processings[i] == null) continue;

            RenderTexture currentDestination = (i == processings.Count - 1) ? destination : temp;
            processings[i].Apply(currentSource, currentDestination);

            // Swap the buffers
            if (i < processings.Count - 1) // Avoid unnecessary copy on the last element
            {
                var swap = currentSource;
                currentSource = temp;
                temp = swap;
            }
        }

        RenderTexture.ReleaseTemporary(temp);
    }
}
