#ifndef _CMakeTarget_module_h_
#define _CMakeTarget_module_h_
 
 #include "Engine.h"
 //#include "UnrealEd.h"
 #include "Modules/ModuleInterface.h"
 #include "Modules/ModuleManager.h"
 
 
 class FCMakeTargetEditorModule : public IModuleInterface
 {
 public:
 
     void StartupModule() override;
 
 
     void ShutdownModule() override;
 
 };

 #endif//_CMakeTarget_module_h_
