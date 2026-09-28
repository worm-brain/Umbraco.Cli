using Microsoft.Kiota.Abstractions.Serialization;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// User-group granular permission models that write the <c>$type</c> discriminator FIRST.
/// Umbraco binds <c>permissions[]</c> as a System.Text.Json polymorphic interface, which only
/// reads the discriminator when it is the object's first property; Kiota's generated
/// serializers write it after the other properties, so Umbraco 17 rejected every user-group
/// create/update carrying a granular permission with "must specify a type discriminator".
/// </summary>
internal static class UserGroupPermissionBodies
{
    /// <summary>
    /// Copies a per-document permission into a model that serializes <c>$type</c> first.
    /// </summary>
    /// <param name="p">The permission, as built or as read from the group.</param>
    /// <returns>The discriminator-first copy.</returns>
    public static Gen.DocumentPermissionPresentationModel Document(
        Gen.DocumentPermissionPresentationModel p
    ) =>
        new DocumentBody
        {
            Type = p.Type ?? nameof(Gen.DocumentPermissionPresentationModel),
            Document = p.Document,
            Verbs = p.Verbs,
        };

    /// <summary>
    /// Copies a property-value permission into a model that serializes <c>$type</c> first.
    /// </summary>
    /// <param name="p">The permission as read from the group, or null.</param>
    /// <returns>The discriminator-first copy, or null when <paramref name="p"/> is null.</returns>
    public static Gen.DocumentPropertyValuePermissionPresentationModel? PropertyValue(
        Gen.DocumentPropertyValuePermissionPresentationModel? p
    ) =>
        p is null
            ? null
            : new PropertyValueBody
            {
                Type = p.Type ?? nameof(Gen.DocumentPropertyValuePermissionPresentationModel),
                DocumentType = p.DocumentType,
                PropertyType = p.PropertyType,
                Verbs = p.Verbs,
            };

    /// <summary>
    /// Copies an unknown-kind permission into a model that serializes <c>$type</c> first.
    /// </summary>
    /// <param name="p">The permission as read from the group, or null.</param>
    /// <returns>The discriminator-first copy, or null when <paramref name="p"/> is null.</returns>
    public static Gen.UnknownTypePermissionPresentationModel? Unknown(
        Gen.UnknownTypePermissionPresentationModel? p
    ) =>
        p is null
            ? null
            : new UnknownBody
            {
                Type = p.Type ?? nameof(Gen.UnknownTypePermissionPresentationModel),
                Context = p.Context,
                Verbs = p.Verbs,
            };

    /// <summary>A per-document permission that writes <c>$type</c> first.</summary>
    private sealed class DocumentBody : Gen.DocumentPermissionPresentationModel
    {
        /// <summary>Writes <c>$type</c>, then the base model's other properties.</summary>
        /// <param name="writer">The Kiota serialization writer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
        public override void Serialize(ISerializationWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue("$type", Type);
            writer.WriteObjectValue("document", Document);
            writer.WriteCollectionOfPrimitiveValues("verbs", Verbs);
        }
    }

    /// <summary>A property-value permission that writes <c>$type</c> first.</summary>
    private sealed class PropertyValueBody : Gen.DocumentPropertyValuePermissionPresentationModel
    {
        /// <summary>Writes <c>$type</c>, then the base model's other properties.</summary>
        /// <param name="writer">The Kiota serialization writer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
        public override void Serialize(ISerializationWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue("$type", Type);
            writer.WriteObjectValue("documentType", DocumentType);
            writer.WriteObjectValue("propertyType", PropertyType);
            writer.WriteCollectionOfPrimitiveValues("verbs", Verbs);
        }
    }

    /// <summary>An unknown-kind permission that writes <c>$type</c> first.</summary>
    private sealed class UnknownBody : Gen.UnknownTypePermissionPresentationModel
    {
        /// <summary>Writes <c>$type</c>, then the base model's other properties.</summary>
        /// <param name="writer">The Kiota serialization writer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
        public override void Serialize(ISerializationWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue("$type", Type);
            writer.WriteStringValue("context", Context);
            writer.WriteCollectionOfPrimitiveValues("verbs", Verbs);
        }
    }
}
