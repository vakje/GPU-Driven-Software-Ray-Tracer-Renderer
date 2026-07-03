# GPU Software Ray Tracing Renderer (work in progress)

## Overview
This project is a real-time GPU ray tracing system built in Unity using ShaderLab and C#. 
It implements a custom rendering pipeline with a focus on performance, acceleration structures, and real-time visualization.


### Performance
- Implements a **bottom-up binned SAH BVH (BVH2)**
- Optimized for fast build and traversal
- Designed for real-time Rendering

## Frames per second
- Handles ~80,000 triangles efficiently
- Achieves ~400 FPS in test scenes (varies by hardware and scene complexity)

## Rendering Technique
- Camera is projected through a screen-space quad
- Custom matrix setup controls ray origin and direction
- BVH traversal performed per ray for intersection testing


## Screenshot renderer in action

<img width="1677" height="1078" alt="image" src="https://github.com/user-attachments/assets/f2188d20-e4ce-4f7e-8ff0-4ec0dbea9f49" />


## Demo Video 
[Watch demo on YouTube](https://youtu.be/wT9bKHxaCXA)

## Niva Lada 2121 FBX model is from sketfab Made by Greg McKechnie