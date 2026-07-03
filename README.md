# GPU Software Ray Tracing Renderer (work in progress)


# Backstory 
This project began as a gameplay feature. We wanted to make a window system that could render non-Euclidean spaces behind a flat quad, without occupying real space in the scene. Initially, I thought about using stencil buffers, but that would have forced me to build duplicate scenes entirely by hand, which lacked the technical depth needed for a diploma project. Instead, I started looking into ray marching and ray tracing to mathematically generate these windows using real FBX geometry. I knew absolutely nothing about ray tracing when I started. Because of that, this project turned into a really cool journey of learning a completely new way to render graphics from scratch.

## Overview
This project is a real-time GPU ray tracing system built in Unity using ShaderLab and C#. 
It implements a custom rendering pipeline with a focus on performance, acceleration structures, and real-time visualization.
version of unity that this project made on (6000.3.10f1)(URP)


### Performance
- Implements a **BLAS bottom-up binned SAH BVH (BVH2)**
- Optimized for fast build and traversal
- Designed for real-time Rendering

### Editor
- implements simple editor which in this version does not work in real time 
- to move objects you need to pre-change postion/angle before running the game 

## Frames per second
- Handles ~80,000 triangles efficiently
- Achieves ~400 FPS in test scenes (varies by hardware and scene complexity)

## Rendering Technique
- Camera is projected through a screen-space quad
- Custom matrix setup controls ray origin and direction
- BVH traversal performed per ray for intersection testing (stack traversal with shortening the longer rays)

## Effect of windows 
- Effect of windows is similar to a stencil buffer 
- In reality, it only imitates the effect, while the actual rendering uses pure ray tracing.

## Screenshot renderer in action

<img width="1677" height="1078" alt="image" src="https://github.com/user-attachments/assets/f2188d20-e4ce-4f7e-8ff0-4ec0dbea9f49" />


## Demo Video 
[Watch demo on YouTube](https://youtu.be/wT9bKHxaCXA)

## Niva Lada 2121 FBX model is from sketfab Made by Greg McKechnie