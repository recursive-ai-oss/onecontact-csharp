namespace OneContact.Data.Blocks;

/// <summary>Hand-written words around the fields: labels, headings, navigation, boilerplate, job titles, note templates and filler prose.
/// English and Canadian French; another language is another <see cref="LexSet"/>.</summary>
internal sealed class LexSet
{
    internal string[] PhoneLabels = [];
    internal string[] FaxLabels = [];
    internal string[] EmailLabels = [];
    internal string[] WebLabels = [];
    internal string[] AddressLabels = [];
    internal string[] ExtLabels = [];
    internal string[] Titles = [];
    internal string[] Departments = [];
    internal string[] Nav = [];
    internal string[] Legal = [];
    internal string[] Headings = [];
    internal string[] TeamHeadings = [];
    internal string[] LocationHeadings = [];
    internal string[] Closings = [];
    internal string[] Greetings = [];
    internal string[] Notes = [];
    internal string[] Ugc = [];
    internal string[] Subjects = [];
    internal string[] Predicates = [];
    internal string[] Topics = [];
    internal string[] Facts = [];
    internal string[] Months = [];
    internal string[] Roles = [];
    internal string[] OfficeNames = [];
    internal string Rights = "";

    internal static readonly LexSet En = new()
    {
        Rights = "All rights reserved.",
        PhoneLabels = ["Tel", "Tel.", "Phone", "Telephone", "Call us", "Call", "Office", "Main", "T", "Ph", "Direct", "Mobile", "Cell", "Toll free", "Phone number", "Contact number"],
        FaxLabels = ["Fax", "F", "Fax number"],
        EmailLabels = ["Email", "E-mail", "Email us", "E", "Write to us", "Mail", "Email address", "Send us an email"],
        WebLabels = ["Web", "Website", "Visit us", "Online", "W", "Web site", "Find us online"],
        AddressLabels = ["Address", "Visit us", "Our office", "Head office", "Mailing address", "Find us", "Location", "Office", "Come see us", "Our address"],
        ExtLabels = [" x", " ext. ", " ext ", " extension ", " #"],
        Titles = ["Sales Manager", "Account Executive", "Customer Service Representative", "Office Manager", "Operations Director", "Chief Executive Officer", "President", "Vice President of Sales", "Marketing Coordinator", "Project Manager", "Senior Accountant", "Controller", "HR Manager", "Receptionist", "Broker", "Attorney", "Partner", "Associate", "Dispatcher", "Service Technician", "Estimator", "Property Manager", "Loan Officer", "Registered Nurse", "Dentist", "Principal", "Superintendent", "Executive Director", "Program Coordinator", "Store Manager", "Paralegal", "Software Engineer", "Director of Engineering", "Financial Advisor", "Realtor", "Owner", "Founder", "General Manager", "Technical Support", "Bookkeeper"],
        Departments = ["Sales", "Customer Service", "Accounts Payable", "Human Resources", "Support", "Billing", "Administration", "Reception", "Marketing", "Shipping and Receiving", "Careers", "Press"],
        Nav = ["Home", "About", "About Us", "Services", "Products", "Careers", "Blog", "News", "Contact", "Contact Us", "FAQ", "Support", "Pricing", "Resources", "Partners", "Login", "Sign in", "Our Team", "Gallery", "Testimonials", "Request a Quote", "Shop"],
        Legal = ["Privacy Policy", "Terms of Use", "Terms & Conditions", "Sitemap", "Accessibility", "Cookie settings", "Follow us on LinkedIn", "Follow us on Facebook", "Powered by WordPress", "Back to top", "Subscribe to our newsletter", "Do not sell my information"],
        Headings = ["Contact us", "Get in touch", "Contact", "Questions? Call us today", "Reach us", "How to find us", "We'd love to hear from you", "Talk to us", "Customer care"],
        TeamHeadings = ["Our team", "Meet the team", "Staff directory", "Leadership", "Our people", "Directory", "Who we are"],
        LocationHeadings = ["Locations", "Our offices", "Branches", "Find a location", "Where to find us"],
        Closings = ["Best regards,", "Thanks,", "Regards,", "Sincerely,", "Cheers,", "--", "Thank you,", "Kind regards,", "Talk soon,", "Sent from my iPhone"],
        Greetings = ["Hi", "Hello", "Dear"],
        Notes =
        [
            "spoke with {P} at {C}, call back on {T}",
            "call me at {T}",
            "reach {P} at {E}",
            "{P} from {C} wants a quote - {E} / {T}",
            "Left voicemail for {P} ({T}). Follow up next week.",
            "New lead: {P}, {C}. Email {E}. Located at {A}.",
            "Mtg w/ {P} re: renewal. tel {T}",
            "Ship to {A} attn {P}",
            "Contact is {P}, {E}",
            "please send invoice to {E}; site {W}",
            "{P} moved to {A}, new number {T}",
            "Their website {W} lists {T}",
            "customer {P} called from {T} about order",
            "forwarded to {P} <{E}>",
            "Referred by {P} of {C}.",
            "{C} - {A} - {T}",
            "{P} ({C}) {T} {E}",
            "Main office {A}. Phone {T}. Fax {T2}.",
            "emailed {E} twice, no reply. try {T}",
            "Update: {P} is no longer at {C}. New contact {P2}, {T}.",
            "Met {P} and {P2} at the show. {C} booth. {W}",
            "billing contact {P} {E}",
            "{P}\n{C}\n{A}\n{T}",
        ],
        Ugc =
        [
            "Great service! Call {T} to book.",
            "I called {C} at {T} and they were very helpful.",
            "Ask for {P} when you get there.",
            "You can visit {C} at {A}.",
            "Their email {E} bounces, use {T} instead.",
            "More info at {W}",
            "Contact them at {W} or {T}",
            "Asked for {P}, got a callback within the hour.",
            "{C} is located at {A} - parking behind the building.",
            "text me {T}",
            "dm me or email {E}",
            "Anyone know {P}? Tried {T} with no luck",
            "Thanks {P}! I'll email you at {E}",
            "Open late. {A}",
            "My number is {T}, ask for {P}",
            "Highly recommend {C}, {W}",
        ],
        Subjects = ["Our team", "The new release", "This guide", "The quarterly report", "Every order", "The workshop", "Our latest collection", "The update", "This recipe", "The committee", "Our warranty", "The trail", "The new menu", "This course"],
        Predicates = ["covers everything you need to know about {t}", "was designed to make {t} simpler", "has been updated with improvements to {t}", "helps you compare options for {t}", "includes a full list of {t}", "is available in three sizes and four colours", "ships within two business days", "is covered for up to five years", "takes about 20 minutes to prepare", "will be held on the first Tuesday of each month", "is free for members", "is explained step by step"],
        Topics = ["home insurance", "kitchen renovation", "tax planning", "winter tires", "garden tools", "small business loans", "pet care", "solar panels", "online banking", "first aid training", "trail maintenance", "car seats"],
        Facts = ["Prices start at $24.99 per month.", "Save 15% on orders over $75.", "Open Monday to Friday, 9 am to 5 pm.", "Free shipping on all orders.", "Rated 4.8 out of 5 by 1,204 customers.", "Offer ends at midnight.", "Over 3,000 products in stock.", "Updated weekly."],
        Months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
        Roles = ["info", "sales", "support", "contact", "hello", "office", "admin", "billing", "service", "team", "orders", "help"],
        OfficeNames = ["Head Office", "Main Office", "Downtown", "North Branch", "West End", "Warehouse", "Showroom", "Service Centre", "Regional Office", "Branch Office"],
    };

    internal static readonly LexSet Fr = new()
    {
        Rights = "Tous droits réservés.",
        PhoneLabels = ["Tél.", "Tél", "Téléphone", "Appelez-nous", "Bureau", "Cellulaire", "Direct", "T", "Sans frais", "Numéro de téléphone", "Composez le"],
        FaxLabels = ["Télécopieur", "Fax", "Téléc."],
        EmailLabels = ["Courriel", "Adresse courriel", "Écrivez-nous", "E-mail", "Envoyez-nous un courriel", "Courrier électronique"],
        WebLabels = ["Site Web", "Web", "Visitez-nous", "En ligne", "Site internet"],
        AddressLabels = ["Adresse", "Notre bureau", "Siège social", "Adresse postale", "Nous trouver", "Emplacement", "Venez nous voir"],
        ExtLabels = [" poste ", " p. ", " #", " ext. "],
        Titles = ["Directeur général", "Directrice des ventes", "Adjointe administrative", "Conseiller en sécurité financière", "Comptable", "Notaire", "Avocat", "Président", "Vice-présidente", "Gestionnaire de projet", "Réceptionniste", "Courtier immobilier", "Technicien", "Responsable du service à la clientèle", "Coordonnatrice marketing", "Ingénieur", "Propriétaire", "Fondateur", "Directeur des opérations", "Estimateur"],
        Departments = ["Ventes", "Service à la clientèle", "Comptes fournisseurs", "Ressources humaines", "Soutien technique", "Facturation", "Administration", "Réception", "Marketing", "Expédition et réception", "Carrières", "Presse"],
        Nav = ["Accueil", "À propos", "Services", "Produits", "Carrières", "Blogue", "Nouvelles", "Contact", "Nous joindre", "FAQ", "Soutien", "Tarifs", "Ressources", "Partenaires", "Connexion", "Notre équipe", "Galerie", "Témoignages", "Demander une soumission", "Boutique"],
        Legal = ["Politique de confidentialité", "Conditions d'utilisation", "Plan du site", "Accessibilité", "Paramètres des témoins", "Suivez-nous sur LinkedIn", "Suivez-nous sur Facebook", "Haut de la page", "Abonnez-vous à notre infolettre"],
        Headings = ["Contactez-nous", "Nous joindre", "Contact", "Des questions ? Appelez-nous", "Écrivez-nous", "Comment nous trouver", "Nous aimerions avoir de vos nouvelles"],
        TeamHeadings = ["Notre équipe", "Faites connaissance avec l'équipe", "Répertoire du personnel", "Direction", "Nos gens"],
        LocationHeadings = ["Emplacements", "Nos bureaux", "Succursales", "Trouver une succursale"],
        Closings = ["Cordialement,", "Merci,", "Bien à vous,", "Salutations distinguées,", "Bonne journée,", "--", "Envoyé de mon iPhone"],
        Greetings = ["Bonjour", "Allô", "Cher"],
        Notes =
        [
            "parlé avec {P} de {C}, rappeler au {T}",
            "contactez {P} au {T} ou à {E}",
            "{P} ({C}) - courriel : {E}",
            "Livraison à {A}, attention {P}",
            "Rappeler {P} au {T}",
            "site web : {W}",
            "Nouveau client : {P}, {C}, {A}",
            "envoyer la facture à {E}",
            "Message laissé à {P} ({T}). Relance la semaine prochaine.",
            "{C} - {A} - {T}",
            "{P} a déménagé au {A}, nouveau numéro {T}",
            "Bureau principal {A}. Tél. {T}. Téléc. {T2}.",
            "écrit à {E} deux fois, sans réponse. essayer {T}",
            "Rencontré {P} et {P2} au salon. Kiosque de {C}. {W}",
            "{P}\n{C}\n{A}\n{T}",
        ],
        Ugc =
        [
            "Excellent service ! Appelez le {T} pour réserver.",
            "J'ai appelé {C} au {T} et ils ont été très serviables.",
            "Demandez {P} en arrivant.",
            "Vous pouvez visiter {C} au {A}.",
            "Leur courriel {E} ne fonctionne plus, utilisez le {T}.",
            "Plus d'info sur {W}",
            "Écrivez-moi à {E}",
            "Mon numéro est le {T}, demandez {P}",
            "Je recommande {C}, {W}",
        ],
        Subjects = ["Notre équipe", "La nouvelle version", "Ce guide", "Le rapport trimestriel", "Chaque commande", "L'atelier", "Notre dernière collection", "La mise à jour", "Cette recette", "Le comité", "Notre garantie", "Le sentier"],
        Predicates = ["couvre tout ce qu'il faut savoir sur {t}", "vise à simplifier {t}", "apporte des améliorations à {t}", "vous aide à comparer les options pour {t}", "comprend la liste complète de {t}", "se décline en trois formats et quatre couleurs", "part sous deux jours ouvrables", "bénéficie d'une garantie de cinq ans", "demande environ 20 minutes de préparation", "aura lieu le premier mardi de chaque mois", "reste gratuit pour les membres"],
        Topics = ["l'assurance habitation", "la rénovation de cuisine", "la planification fiscale", "les pneus d'hiver", "les outils de jardin", "les prêts aux petites entreprises", "les soins aux animaux", "les panneaux solaires", "les services bancaires en ligne", "les premiers soins"],
        Facts = ["Prix à partir de 24,99 $ par mois.", "Économisez 15 % sur les commandes de plus de 75 $.", "Ouvert du lundi au vendredi, de 9 h à 17 h.", "Livraison gratuite sur toutes les commandes.", "Noté 4,8 sur 5 par 1 204 clients.", "L'offre se termine à minuit.", "Plus de 3 000 produits en stock.", "Mis à jour chaque semaine."],
        Months = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"],
        Roles = ["info", "ventes", "soutien", "contact", "bonjour", "bureau", "admin", "facturation", "service", "equipe", "commandes", "accueil"],
        OfficeNames = ["Siège social", "Bureau principal", "Centre-ville", "Succursale nord", "Entrepôt", "Salle de montre", "Centre de service", "Bureau régional"],
    };

    internal static LexSet Of(Loc loc) => loc == Loc.CaFr ? Fr : En;
}
