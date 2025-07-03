# Quick start

## Example Scene Overview

The package includes a sample scene to demonstrate a working cloud setup.  
To explore it, open the provided scene file located in `Examples/Clouds`.

![Clouds Render](Images/Wallpaper%202.png)

---
### CloudsPostProcess

This `MonoBehaviour` script drives the cloud rendering.  

**Required references:**
- A `CloudSettings` preset.
- A `Directional Light` in the scene (the sun).
- A `TransmittanceMap` component to compute lighting information.

![Inspector](Images/Inspector%20Post%20Process.png)

---
### PostProcessStack

Attached to the main camera, this component manages an array of post-processing effects that are applied sequentially once rendering is complete.

To render clouds, make sure a `CloudsPostProcess` instance is added to the stack.

![Inspector](Images/Inspector%20Post%20Process%20Stack.png)

---

### TransmittanceMap

This component computes and stores volumetric lighting data in a 3D texture (also referred to as a transmittance texture).  This texture is sampled by the cloud shader to approximate lighting across the volume.

Below is a visual example using a low-resolution map:

![Inspector](Images/Inspector%20TransmittanceMap.png)

#### Configuration

| Property             | Description                                                                                                                                                                                                      |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Map Updates**      | Lighting is computed once at initialization. Enable `Calculate Lighting Each Frame` to update it in real time.                                                                                                   |
| **Map Resolution**   | Defines the voxel resolution of the transmittance texture. Higher resolutions improve quality but will reduce performance if updated each frame.                    |
| **Lighting Quality** | Controls the minimum step size used by light rays during calculation. Lower values yield more accurate lighting but are slower to compute. If not updating in real time, a low value (e.g. `25`) is recommended. |

---

>For additional customization options, including lighting, fog, shadows, and quality settings, refer to the [Cloud Rendering Configuration Guide](./Cloud%20Rendering%20Configuration%20Guide.md).
