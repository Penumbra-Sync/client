using CheapLoc;

namespace MareSynchronos.Localization
{
    public static class Strings
    {
        public class ToSStrings
        {
            public readonly string AgreementLabel = Loc.Localize("AgreementLabel", "Agreement of Usage of Service");
            public readonly string ReadLabel = Loc.Localize("ReadLabel", "READ THIS CAREFULLY");

            public readonly string Paragraph1 = Loc.Localize("Paragraph1",
                "All of the mod files currently active on your character as well as your current character state will be uploaded to the service you registered yourself at automatically. " +
                "The plugin will exclusively upload the necessary mod files and not the whole mod.");

            public readonly string Paragraph2 = Loc.Localize("Paragraph2",
                "If you are on a data capped internet connection, higher fees due to data usage depending on the amount of downloaded and uploaded mod files might occur. " +
                "Mod files will be compressed on up- and download to save on bandwidth usage. Due to varying up- and download speeds, changes in characters might not be visible immediately. " +
                "Files present on the service that already represent your active mod files will not be uploaded again.");

            public readonly string Paragraph3 = Loc.Localize("Paragraph3",
                "The mod files you are uploading are confidential and will not be distributed to parties other than the ones who are requesting the exact same mod files. " +
                "Please think about who you are going to pair since it is unavoidable that they will receive and locally cache the necessary mod files that you have currently in use. " +
                "Locally cached mod files will have arbitrary file names to discourage attempts at replicating the original mod.");

            public readonly string Paragraph4 = Loc.Localize("Paragraph4",
                "The plugin creator tried their best to keep you secure. However, there is no guarantee for 100% security. Do not blindly pair your client with everyone.");

            public readonly string Paragraph5 = Loc.Localize("Paragraph5",
                "Mod files that are saved on the service will remain on the service as long as there are requests for the files from clients. " +
                "After a period of not being used, the mod files will be automatically deleted. " +
                "You will also be able to wipe all the files you have personally uploaded on request. " +
                "The service holds no information about which mod files belong to which mod.");

            public readonly string Paragraph6 = Loc.Localize("Paragraph6",
                "This service is provided as-is. In case of abuse, contact darkarchon#4313 on Discord or join the Mare Synchronos Discord. " +
                "To accept those conditions hold CTRL while clicking 'I agree'");

            public readonly string AgreeLabel = Loc.Localize("AgreeLabel", "I agree");

            public readonly string RemainingLabel = Loc.Localize("RemainingLabel", "remaining");

            public readonly string FailedLabel = Loc.Localize("FailedLabel",
                "Congratulations. You have failed to read the agreements.");

            public readonly string TimeoutLabel = Loc.Localize("TimeoutLabel",
                "I'm going to give you 1 minute to read the agreements carefully again. If you fail once more you will have to solve an annoying puzzle.");

            public readonly string FailedAgainLabel = Loc.Localize("FailedAgainLabel",
                "Congratulations. You have failed to read the agreements. Again.");

            public readonly string PuzzleLabel = Loc.Localize("PuzzleLabel",
                "I did warn you. Here's your annoying puzzle:");

            public readonly string PuzzleDescLabel = Loc.Localize("PuzzleDescLabel",
                "Enter the following 3 words from the agreement exactly as described without punctuation to make the \"I agree\" button visible again.");

            public readonly string ParagraphLabel = Loc.Localize("ParagraphLabel", "Paragraph");

            public readonly string SentenceLabel = Loc.Localize("SentenceLabel", "Sentence");

            public readonly string WordLabel = Loc.Localize("WordLabel", "Word");
        }
        
        public static ToSStrings ToS = new();
    }
}