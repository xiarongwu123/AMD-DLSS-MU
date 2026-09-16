// MIT. RenoDX adapter attribution and license: LICENSE-Swapper.
// Native ReShade panel, independent of the desktop fallback. No DLL injection.
#include <imgui.h>
#include <reshade.hpp>
#include <array>
#include <vector>
#include <string>
#include <cmath>
#include <sstream>
#include <locale>
#include <unordered_map>
#include <memory>
#include <cstring>
namespace lab_live {
struct command { uint32_t epoch, id, kind; float value; };
inline std::string quoted(const std::string &v) { std::string r="\""; for(char c:v){if(c=='"'||c=='\\')r+='\\'; if(c>=32)r+=c;}return r+'"'; }
}
#include "renodx-ui-bridge.hpp"
static_assert(IMGUI_VERSION_NUM == 19250, "Only tested with pinned ReShade/ImGui ABI");
extern "C" __declspec(dllexport) const char *NAME = "AMD DLSS MU Live Panel";
extern "C" __declspec(dllexport) const char *DESCRIPTION = "F8 live controls for verified RenoDX v4.7 only. AMD official runtime is not controlled by this adapter.";
namespace {
struct state { nr_live::controls controls; bool open=false; };
std::unordered_map<reshade::api::effect_runtime*,std::unique_ptr<state>> states;
bool registered=false;
void draw(reshade::api::effect_runtime *runtime) {
    auto &ptr=states[runtime]; if(!ptr)ptr=std::make_unique<state>(); auto &s=*ptr;
    if(runtime->is_key_pressed(VK_F8))s.open=!s.open;
    if(runtime->is_key_pressed(VK_ESCAPE))s.open=false;
    // Run while there are pending changes so the original callback reads them back.
    bool pending=false; for(auto &f:s.controls.fields)pending|=f.pending||f.confirming;
    if(!s.open&&!pending)return;
    s.controls.tick(runtime);
    if(!s.open)return;
    runtime->block_input_next_frame(); ImGui::GetIO().MouseDrawCursor=true;
    ImGui::SetNextWindowSize(ImVec2(480,620),ImGuiCond_FirstUseEver);
    ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding,12.f);
    ImGui::PushStyleColor(ImGuiCol_WindowBg,ImVec4(.065f,.085f,.075f,.97f));
    ImGui::PushStyleColor(ImGuiCol_SliderGrab,ImVec4(.40f,.80f,.18f,1.f));
    ImGui::PushStyleColor(ImGuiCol_CheckMark,ImVec4(.40f,.80f,.18f,1.f));
    if(ImGui::Begin("AMD DLSS MU | Live controls",&s.open)) {
        ImGui::TextUnformatted("F8 / Escape: close | Values read from the runtime");
        ImGui::Separator();
        if(!s.controls.active){ImGui::TextWrapped("Not connected: %s",s.controls.reason.c_str()); ImGui::TextWrapped("Requires the hash-pinned RenoDX v4.7 and ReShade 6.8.0 add-on build. AMD mode 1: use the original End menu. OptiScaler: use Insert.");}
        else ImGui::TextColored(ImVec4(.4f,.9f,.3f,1.f),"RenoDX connected (not proof of neural rendering)");
        for(size_t i=0;i<s.controls.fields.size();++i){
            auto &f=s.controls.fields[i]; float value=f.value; bool changed=false;
            ImGui::PushID(int(i)); ImGui::BeginDisabled(!s.controls.active||!f.seen);
            if(f.kind==1){bool on=value!=0; changed=ImGui::Checkbox(f.label,&on);value=on?1.f:0.f;}
            else if(f.kind==4){std::vector<const char*> names;for(auto &n:f.options)names.push_back(n.c_str()); int selected=int(value); if(!names.empty()){changed=ImGui::Combo(f.label,&selected,names.data(),int(names.size()));value=float(selected);}else ImGui::TextUnformatted(f.label);}
            else changed=ImGui::SliderFloat(f.label,&value,f.min,f.max,"%.3f");
            if(changed)s.controls.accept(1,{1,uint32_t(101+i),f.kind,value});
            ImGui::EndDisabled(); ImGui::PopID();
        }
        ImGui::Separator(); ImGui::TextWrapped("Changes use the original RenoDX callback and are read back on the next callback. Restore game files in the launcher. Diagnostics include ReShade.log.");
    }
    ImGui::End(); ImGui::PopStyleColor(3); ImGui::PopStyleVar();
}
void destroy(reshade::api::effect_runtime *r){states.erase(r);}
}
extern "C" __declspec(dllexport) bool AddonInit(HMODULE addon,HMODULE reshadeModule){
    if(registered)return true;
    if(!reshade::register_addon(addon,reshadeModule))return false;
    reshade::register_event<reshade::addon_event::reshade_overlay>(draw);
    reshade::register_event<reshade::addon_event::destroy_effect_runtime>(destroy);
    registered=true;return true;
}
extern "C" __declspec(dllexport) void AddonUninit(HMODULE addon,HMODULE reshadeModule){
    if(!registered)return;
    reshade::unregister_event<reshade::addon_event::reshade_overlay>(draw);
    reshade::unregister_event<reshade::addon_event::destroy_effect_runtime>(destroy);
    states.clear();reshade::unregister_addon(addon,reshadeModule);registered=false;
}
