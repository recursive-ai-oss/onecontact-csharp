// Black-box driver for the reference address library. It uses only the library's public classes:
// the data comes from a map of key -> JSON text handed to the public AddressVerificationData data source (so the
// reference reads the pinned data file offline), the validator is StandardAddressVerifier over FieldVerifier, and
// the formatter is FormatInterpreter.
//
//   java -cp common.jar:json.jar:. AddressOracle <countryinfo.txt> < cases.tsv > results.tsv
//   java -cp common.jar:json.jar:. AddressOracle <countryinfo.txt> --format-map > formats.tsv
//
// Case line (tab separated, "\N" for an absent value, street lines joined with U+0001):
//   id, country, language, admin area, locality, dependent locality, postal code, sorting code, organization, recipient, lines
// Result line:
//   id, problems ("FIELD:KIND" sorted, joined with ";"), envelope lines (joined with U+0001), or "!EXCEPTION:<class>"
import com.google.i18n.addressinput.common.*;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

public final class AddressOracle {
    public static void main(String[] args) throws Exception {
        Map<String, String> data = new HashMap<>();
        for (String line : Files.readAllLines(Paths.get(args[0]), StandardCharsets.UTF_8)) {
            int eq = line.indexOf('=');
            if (eq < 0) continue;
            String key = line.substring(0, eq);
            if (key.equals("data") || key.startsWith("data/")) data.put(key, line.substring(eq + 1));
        }
        PrintStream out = new PrintStream(new FileOutputStream(FileDescriptor.out), false, "UTF-8");

        if (args.length > 1 && args[1].equals("--format-map")) {
            TreeMap<String, String> sorted = new TreeMap<>(RegionDataConstants.getCountryFormatMap());
            for (Map.Entry<String, String> e : sorted.entrySet()) {
                out.print(e.getKey() + "\t" + e.getValue().replace("\n", "\\n") + "\n");
            }
            out.flush();
            return;
        }

        AddressVerificationData source = new AddressVerificationData(data);
        StandardAddressVerifier verifier = new StandardAddressVerifier(new FieldVerifier(source));
        FormatInterpreter formatter = new FormatInterpreter(new FormOptions().createSnapshot());

        BufferedReader in = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8));
        String line;
        while ((line = in.readLine()) != null) {
            if (line.isEmpty()) continue;
            String[] f = line.split("\t", -1);
            StringBuilder sb = new StringBuilder(f[0]).append('\t');
            AddressData address = null;
            try {
                AddressData.Builder b = new AddressData.Builder();
                if (v(f[1]) != null) b.setCountry(v(f[1]));
                if (v(f[2]) != null) b.setLanguageCode(v(f[2]));
                if (v(f[3]) != null) b.setAdminArea(v(f[3]));
                if (v(f[4]) != null) b.setLocality(v(f[4]));
                if (v(f[5]) != null) b.setDependentLocality(v(f[5]));
                if (v(f[6]) != null) b.setPostalCode(v(f[6]));
                if (v(f[7]) != null) b.setSortingCode(v(f[7]));
                if (v(f[8]) != null) b.setOrganization(v(f[8]));
                if (v(f[9]) != null) b.setRecipient(v(f[9]));
                if (!f[10].equals("\\N")) {
                    b.setAddressLines(new ArrayList<>(Arrays.asList(f[10].split("\u0001", -1))));
                }
                address = b.build();
            } catch (Throwable t) {
                sb.append("!BUILD:").append(t.getClass().getSimpleName()).append('\t');
            }
            if (address != null) {
                try {
                    AddressProblems problems = new AddressProblems();
                    verifier.verify(address, problems);
                    List<String> items = new ArrayList<>();
                    for (Map.Entry<AddressField, AddressProblemType> e : problems.getProblems().entrySet()) {
                        items.add(e.getKey().name() + ":" + e.getValue().name());
                    }
                    Collections.sort(items);
                    sb.append(String.join(";", items));
                } catch (Throwable t) {
                    sb.append("!VERIFY:").append(t.getClass().getSimpleName());
                }
                sb.append('\t');
                try {
                    sb.append(String.join("\u0001", formatter.getEnvelopeAddress(address)));
                } catch (Throwable t) {
                    sb.append("!FORMAT:").append(t.getClass().getSimpleName());
                }
            }
            out.print(sb.append('\n'));
        }
        out.flush();
    }

    private static String v(String s) {
        return s.equals("\\N") ? null : s;
    }
}
