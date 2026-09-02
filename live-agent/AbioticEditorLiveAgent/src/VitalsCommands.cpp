// See VitalsCommands.h for the "not yet verified against the real game" disclaimer - this file
// needs the real UE4SS C++ Mod SDK to compile at all, which was not available in the session
// that wrote it.
#include "VitalsCommands.h"

#include <DynamicOutput/DynamicOutput.hpp>
#include <Unreal/UObjectGlobals.hpp>
#include <Unreal/UObject.hpp>
#include <Unreal/World.hpp>
#include <Unreal/AActor.hpp>
#include <Unreal/Property/NumericPropertyTypes.hpp>

using namespace RC;
using namespace RC::Unreal;

namespace LiveAgent
{
    namespace
    {
        // Finds the running game's single local player's PlayerState. Abiotic Factor is a
        // co-op survival game (server-authoritative, per this repo's own CLAUDE.md), so "the
        // live agent's own process" is either the locally-hosted client (edits itself) or a
        // dedicated server (the operator would need a player-selection command to target one of
        // several connected players - out of scope for this Phase-0 vitals slice, which only
        // ever reads/writes whichever PlayerState this lookup happens to find first).
        UObject* FindLocalPlayerState()
        {
            // AbioticCharacterPlayerState (or similar - confirm the exact class name against a
            // live object dump, e.g. via UE4SS's own "generate_luasdk"/object-dump console
            // command) is the blueprint-generated class the save reader/writer already know as
            // "Abiotic_CharacterSave" on the file side (see Core/Serialization/Player). The
            // live class name is not necessarily identical to the save class name and MUST be
            // confirmed, not assumed.
            std::vector<UObject*> matches;
            UObjectGlobals::FindAllOf(STR("AbioticCharacterPlayerState"), matches);
            return matches.empty() ? nullptr : matches.front();
        }

        // Mirrors PropertyTagExtensions.FindByPrefix on the .NET side (Serialization/Gvas): the
        // live reflection property carries the same blueprint-compiler hash suffix the save
        // property does (both come from the same compiled blueprint class), so a live lookup
        // needs the identical "match by prefix, not exact name" discipline CLAUDE.md documents
        // for the file format - a hardcoded exact name would break on the next game patch the
        // same way a save writer's exact-name lookup would.
        FProperty* FindPropertyByPrefix(UObject* object, const std::wstring& prefix)
        {
            if (!object) return nullptr;
            for (FProperty* property : object->GetClassPrivate()->ForEachProperty())
            {
                if (property->GetName().starts_with(prefix)) return property;
            }
            return nullptr;
        }

        double GetDoubleByPrefix(UObject* object, const std::wstring& prefix, double fallback)
        {
            FProperty* property = FindPropertyByPrefix(object, prefix);
            if (!property) return fallback;
            if (auto* numeric = CastField<FDoubleProperty>(property))
                return numeric->GetPropertyValue(numeric->ContainerPtrToValuePtr<void>(object));
            if (auto* numeric = CastField<FFloatProperty>(property))
                return static_cast<double>(numeric->GetPropertyValue(numeric->ContainerPtrToValuePtr<void>(object)));
            return fallback;
        }

        void SetDoubleByPrefix(UObject* object, const std::wstring& prefix, double value)
        {
            FProperty* property = FindPropertyByPrefix(object, prefix);
            if (!property) return; // Unknown on this game build: leave it alone, do not guess a class.
            if (auto* numeric = CastField<FDoubleProperty>(property))
                numeric->SetPropertyValue(numeric->ContainerPtrToValuePtr<void>(object), value);
            else if (auto* numeric = CastField<FFloatProperty>(property))
                numeric->SetPropertyValue(numeric->ContainerPtrToValuePtr<void>(object), static_cast<float>(value));
        }
    }

    void RegisterVitalsCommands(Server& server)
    {
        server.RegisterCommand("vitals.get", [](const JsonValue&) -> JsonValue {
            UObject* playerState = FindLocalPlayerState();
            if (!playerState) throw CommandFailed("no local player state found");

            JsonObject result;
            result.emplace("hunger", GetDoubleByPrefix(playerState, STR("Hunger_"), 100.0));
            result.emplace("thirst", GetDoubleByPrefix(playerState, STR("Thirst_"), 100.0));
            result.emplace("sanity", GetDoubleByPrefix(playerState, STR("Sanity_"), 100.0));
            result.emplace("fatigue", GetDoubleByPrefix(playerState, STR("Fatigue_"), 0.0));
            result.emplace("continence", GetDoubleByPrefix(playerState, STR("Continence_"), 100.0));
            result.emplace("money", GetDoubleByPrefix(playerState, STR("Money_"), 0.0));
            result.emplace("head", GetDoubleByPrefix(playerState, STR("Head_"), 100.0));
            result.emplace("torso", GetDoubleByPrefix(playerState, STR("Torso_"), 100.0));
            result.emplace("leftArm", GetDoubleByPrefix(playerState, STR("LeftArm_"), 100.0));
            result.emplace("rightArm", GetDoubleByPrefix(playerState, STR("RightArm_"), 100.0));
            result.emplace("leftLeg", GetDoubleByPrefix(playerState, STR("LeftLeg_"), 100.0));
            result.emplace("rightLeg", GetDoubleByPrefix(playerState, STR("RightLeg_"), 100.0));
            return JsonValue(std::move(result));
        });

        server.RegisterCommand("vitals.set", [](const JsonValue& payload) -> JsonValue {
            UObject* playerState = FindLocalPlayerState();
            if (!playerState) throw CommandFailed("no local player state found");
            const JsonObject* object = payload.AsObject();
            if (!object) throw CommandFailed("vitals.set needs a payload object");

            auto apply = [&](const char* key, const std::wstring& prefix) {
                auto it = object->find(key);
                if (it != object->end()) SetDoubleByPrefix(playerState, prefix, it->second.AsNumber());
            };
            apply("hunger", STR("Hunger_"));
            apply("thirst", STR("Thirst_"));
            apply("sanity", STR("Sanity_"));
            apply("fatigue", STR("Fatigue_"));
            apply("continence", STR("Continence_"));
            apply("money", STR("Money_"));
            apply("head", STR("Head_"));
            apply("torso", STR("Torso_"));
            apply("leftArm", STR("LeftArm_"));
            apply("rightArm", STR("RightArm_"));
            apply("leftLeg", STR("LeftLeg_"));
            apply("rightLeg", STR("RightLeg_"));
            return JsonValue();
        });
    }
}
