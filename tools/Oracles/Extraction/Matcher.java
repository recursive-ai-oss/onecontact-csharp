// Runs libphonenumber's matcher (PhoneNumberUtil.findNumbers at the VALID leniency) over {id, text, country} lines and writes the
// spans in the extraction scorer's format. The released jar is only run; offsets are UTF-16 code units, as in the input strings.
//
//   java -cp libphonenumber-9.0.43.jar:json-20240303.jar Matcher.java <in.jsonl> <out.jsonl>
import com.google.i18n.phonenumbers.PhoneNumberMatch;
import com.google.i18n.phonenumbers.PhoneNumberUtil;
import com.google.i18n.phonenumbers.PhoneNumberUtil.Leniency;
import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import org.json.JSONArray;
import org.json.JSONObject;

public class Matcher {
    public static void main(String[] args) throws Exception {
        PhoneNumberUtil util = PhoneNumberUtil.getInstance();
        int items = 0, spans = 0;
        try (BufferedReader in = Files.newBufferedReader(Paths.get(args[0]), StandardCharsets.UTF_8);
             BufferedWriter out = Files.newBufferedWriter(Paths.get(args[1]), StandardCharsets.UTF_8)) {
            String line;
            while ((line = in.readLine()) != null) {
                if (line.isBlank()) continue;
                JSONObject item = new JSONObject(line);
                String text = item.getString("text");
                String region = item.optString("country", "US");
                JSONArray fields = new JSONArray();
                for (PhoneNumberMatch m : util.findNumbers(text, region, Leniency.VALID, Long.MAX_VALUE)) {
                    JSONObject f = new JSONObject();
                    f.put("type", "phone");
                    f.put("start", m.start());
                    f.put("end", m.end());
                    f.put("text", m.rawString());
                    fields.put(f);
                    spans++;
                }
                JSONObject o = new JSONObject();
                o.put("id", item.getString("id"));
                o.put("fields", fields);
                out.write(o.toString());
                out.newLine();
                items++;
            }
        }
        System.out.println("items " + items + " spans " + spans);
    }
}
