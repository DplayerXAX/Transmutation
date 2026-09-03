using System;

namespace Tutorial604
{
    [Serializable]
    public struct ClumpParameters
    {
        public float pullToCentre;        // 控制草叶朝向簇中心的程度，值越高草叶越集中在簇中心
        public float pointInSameDirection; // 控制簇内草叶朝向的一致性，值越高簇内草叶朝向越统一
        public float baseHeight;          // 草叶的基础高度
        public float heightRandom;        // 草叶高度的随机变化范围
        public float baseWidth;           // 草叶的基础宽度
        public float widthRandom;         // 草叶宽度的随机变化范围
        public float baseTilt;            // 草叶的基础倾斜度，控制顶端偏离垂直方向的程度
        public float tiltRandom;          // 草叶倾斜度的随机变化范围
        public float baseBend;            // 草叶的基础弯曲度，控制整体曲线形状
        public float bendRandom;          // 草叶弯曲度的随机变化范围
    }
}