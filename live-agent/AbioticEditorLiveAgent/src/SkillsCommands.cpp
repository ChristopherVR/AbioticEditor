// See SkillsCommands.h / VitalsCommands.h for the "not yet verified against the real game"
// disclaimer - this file needs the real UE4SS C++ Mod SDK to compile at all.
#include "SkillsCommands.h"

#include <Unreal/UObjectGlobals.hpp>
#include <Unreal/UObject.hpp>
#include <Unreal/Property/ArrayProperty.hpp>
#include <Unreal/Property/StructProperty.hpp>
#include <Unreal/Property/NumericPropertyTypes.hpp>
#include <Unreal/FProperty.hpp>

using namespace RC;
using namespace RC::Unreal;

namespace LiveAgent
{
    namespace
    {
        // Same "no local player selection yet" simplification as VitalsCommands.cpp - see its
        // comment on FindLocalPlayerState (duplicated here rather than shared, since Phase 1's
        // scope is one area's worth of code following the established pattern, not a shared
        // "current player" abstraction yet - that generalization is a Phase 2+ concern once a
        // third area needs it too, per the repo's own "don't build for hypothetical future
        // requirements" convention).
        UObject* FindLocalPlayerState()
        {
            std::vector<UObject*> matches;
            UObjectGlobals::FindAllOf(STR("AbioticCharacterPlayerState"), matches);
            return matches.empty() ? nullptr : matches.front();
        }

        // Mirrors PlayerSaveReader.ReadSkills / PlayerSaveWriter.ApplySkills on the .NET side
        // (Core/Serialization/Player/PlayerSaveWriter.Stats.cs): the "Skills_" array is a fixed
        // list of structs, one per skill, matched by ARRAY INDEX (not by name - skill structs
        // are not individually named properties). FindByPrefix locates the array property
        // itself; each element is then read/written by its known field names within the struct.
        FArrayProperty* FindSkillsArray(UObject* playerState)
        {
            if (!playerState) return nullptr;
            for (FProperty* property : playerState->GetClassPrivate()->ForEachProperty())
            {
                if (property->GetName().starts_with(STR("Skills_")))
                    return CastField<FArrayProperty>(property);
            }
            return nullptr;
        }

        float GetFloatField(void* structPtr, FStructProperty* structType, const std::wstring& prefix, float fallback)
        {
            for (FProperty* field : structType->GetStruct()->ForEachProperty())
            {
                if (!field->GetName().starts_with(prefix)) continue;
                if (auto* numeric = CastField<FFloatProperty>(field))
                    return numeric->GetPropertyValue(numeric->ContainerPtrToValuePtr<void>(structPtr));
            }
            return fallback;
        }

        void SetFloatField(void* structPtr, FStructProperty* structType, const std::wstring& prefix, float value)
        {
            for (FProperty* field : structType->GetStruct()->ForEachProperty())
            {
                if (!field->GetName().starts_with(prefix)) continue;
                if (auto* numeric = CastField<FFloatProperty>(field))
                    numeric->SetPropertyValue(numeric->ContainerPtrToValuePtr<void>(structPtr), value);
                return;
            }
        }
    }

    void RegisterSkillsCommands(Server& server)
    {
        server.RegisterCommand("skills.get", [](const JsonValue&) -> JsonValue {
            UObject* playerState = FindLocalPlayerState();
            if (!playerState) throw CommandFailed("no local player state found");
            FArrayProperty* skillsArray = FindSkillsArray(playerState);
            if (!skillsArray) throw CommandFailed("no Skills_ array property found");
            auto* elementType = CastField<FStructProperty>(skillsArray->GetInnerProperty());
            if (!elementType) throw CommandFailed("Skills_ array element type was not a struct");

            FScriptArray* array = skillsArray->ContainerPtrToValuePtr<FScriptArray>(playerState);
            JsonArray rows;
            for (int32_t i = 0; i < array->Num(); ++i)
            {
                void* element = static_cast<uint8_t*>(array->GetData()) + (i * elementType->GetElementSize());
                JsonObject row;
                row.emplace("index", i);
                row.emplace("xp", GetFloatField(element, elementType, STR("CurrentSkillXP_"), 0.0f));
                row.emplace("xpMultiplier", GetFloatField(element, elementType, STR("CurrentXPMultiplier_"), 1.0f));
                rows.push_back(JsonValue(std::move(row)));
            }
            return JsonValue(std::move(rows));
        });

        server.RegisterCommand("skills.set", [](const JsonValue& payload) -> JsonValue {
            UObject* playerState = FindLocalPlayerState();
            if (!playerState) throw CommandFailed("no local player state found");
            FArrayProperty* skillsArray = FindSkillsArray(playerState);
            if (!skillsArray) throw CommandFailed("no Skills_ array property found");
            auto* elementType = CastField<FStructProperty>(skillsArray->GetInnerProperty());
            if (!elementType) throw CommandFailed("Skills_ array element type was not a struct");
            const JsonArray* rows = payload.AsArray();
            if (!rows) throw CommandFailed("skills.set needs a payload array");

            FScriptArray* array = skillsArray->ContainerPtrToValuePtr<FScriptArray>(playerState);
            for (const auto& row : *rows)
            {
                const JsonObject* rowObject = row.AsObject();
                if (!rowObject) continue;
                int32_t index = static_cast<int32_t>(rowObject->at("index").AsNumber());
                if (index < 0 || index >= array->Num()) continue; // Unknown on this build: skip, do not resize.
                void* element = static_cast<uint8_t*>(array->GetData()) + (index * elementType->GetElementSize());
                if (auto it = rowObject->find("xp"); it != rowObject->end())
                    SetFloatField(element, elementType, STR("CurrentSkillXP_"), static_cast<float>(it->second.AsNumber()));
                if (auto it = rowObject->find("xpMultiplier"); it != rowObject->end())
                    SetFloatField(element, elementType, STR("CurrentXPMultiplier_"), static_cast<float>(it->second.AsNumber()));
            }
            return JsonValue();
        });
    }
}
