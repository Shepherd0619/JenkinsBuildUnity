# JenkinsBuildUnity
A little script that connect Unity (with HybridCLR hot update) and Jenkins together.

# Goal
To make CI/CD more easier to be integrated into any Unity project has hot update feature.

# What is CI/CD?
In software engineering, CI/CD or CICD is the combined practices of continuous integration (CI) and continuous delivery (CD) or, less often, continuous deployment. They are sometimes referred to collectively as continuous development or continuous software development. -- Wikipedia

Alright, in humans word, a computer that keeps building the app till death and you don't have to pay the salary to do so.

# What is Hot Update?
Hotfixes can also solve many of the same issues as a patch, but it is applied to a “hot” system—a live system—to fix an issue:

1. Immediately
2. Without creating system downtimes or outages.

Hotfixes are also known as QFE updates, short for quick-fix engineering updates, a name that illustrates the urgency.

Normally, you’ll create a hotfix quickly, as an urgent measure against issues that need to be fixed immediately and outside of your normal development flow. Unlike patches, hotfixes address very specific issues like adding a new feature, bug, or security fix

-- Chrissy Kidd from bmc.com

In humans word, a solution that only build the modified part and deliver it to players/testers/your boss/... as fast as possible.

# Why do I need CI/CD?
1. You will no longer have to pause your current work because of building on your PC.
2. You will no longer have to check your collaborator's commit manually.
3. Testers/boss can build the client by themselves, instead of a phone call asking you to put down what you are doing and go help them.
4. ......

# Why do I need Hot Update?
1. Push fixes to players as quickly as possible without waiting for App Store/Google Play's review.
2. No need to compile the full client over and over again which drive many of you crazy.
3. ......

# How to use?
In simple, it should be like the following:
1. Install [Jenkins](https://www.jenkins.io/) and unity3d plugin on your server or another PC.
2. Import [HybridCLR](https://github.com/focus-creative-games/hybridclr_unity) and Addressables into your Unity project and do some configurations.
3. Place "JenkinsBuild.cs" into "Assets/Editor".
4. Add a Addressables group called "DLLs" and labels "HotUpdateDLL" and "AOTMetadataDLL".
5. Start HybridCLR build.
6. ~~Copy and paste the dlls under "HybridCLRData/HotUpdateDlls" and "HybridCLRData/AssembliesPostIl2CppStrip" the project need. (**No need to copy all of them.** )~~
7. ~~Add ".bytes" to the end of the name of dll you copied.~~
~~Like "Assembly-CSharp.dll.bytes".~~
8. ~~Add those dlls into "DLLs" group and add the corresponding label to them.~~

No need to do 6, 7, 8 anymore since the script can do them automatically. 
**Make sure your HybridCLRSettings and Addressables Settings are correct since the script do these jobs depends on them.**

9. Create a new Assembly Definition and a script to download & apply updates before loading into main scene.
(**Example scripts included!** )
10. Start Jenkins job and enjoy!
For unity editor command line, please type like this:
```-nographics -batchmode -quit -executeMethod JenkinsBuild.BuildHotUpdateForWindows64```

For details, you can visit the following docs and tutorials created by others to understand the content above better:
1. https://www.youtube.com/watch?v=WdIG0af7S0g
2. https://hybridclr.doc.code-philosophy.com/en/docs/basic
3. https://docs.unity3d.com/Packages/com.unity.addressables@1.20/manual/index.html

Still feel confused? ~~You may wait for my step by step tutorial on [YouTube](https://www.youtube.com/channel/UCRQdc3lSimZvrvIAkt3bTuw) about it (**should be coming soon with an example project**).~~

**I'm not very good at video editing, but don't worry, I will use Markdown documents and sample projects to do the tutorial instead.**

For Chinese, there are already tutorials on my CSDN blog!

来自中国的朋友！你们可以访问我的CSDN博客来获取相关教程！

1. https://blog.csdn.net/u012587406/article/details/135260464
2. https://blog.csdn.net/u012587406/article/details/135441946

# References
1. [0oSTrAngeRo0/AutoBuild](https://github.com/0oSTrAngeRo0/AutoBuild) for command line args.
