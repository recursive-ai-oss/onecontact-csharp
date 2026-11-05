// Probe: which language codes does the geocoder of a released libphonenumber release answer with English place names for? Prints, for
// every ISO 639 code the JDK knows, the description of a Munich number and of a German mobile number. It is only executed.
import com.google.i18n.phonenumbers.PhoneNumberUtil;
import com.google.i18n.phonenumbers.Phonenumber.PhoneNumber;
import com.google.i18n.phonenumbers.geocoding.PhoneNumberOfflineGeocoder;
import java.io.PrintStream;
import java.util.Locale;

public class LangScan {
  public static void main(String[] args) throws Exception {
    PhoneNumberOfflineGeocoder geo = PhoneNumberOfflineGeocoder.getInstance();
    PhoneNumber munich = new PhoneNumber().setCountryCode(49).setNationalNumber(8912345678L);
    PhoneNumber mobile = new PhoneNumber().setCountryCode(49).setNationalNumber(15112345678L);
    PrintStream out = new PrintStream(System.out, false, "UTF-8");
    for (String l : Locale.getISOLanguages()) {
      Locale loc = Locale.forLanguageTag(l);
      String a = geo.getDescriptionForNumber(munich, loc);
      String b = geo.getDescriptionForNumber(mobile, loc);
      out.println(l + "\t" + (a.equals("Munich") ? "EN" : "other:" + a.length()) + "\t" + (b.equals("Germany") ? "EN" : "local"));
    }
    out.flush();
  }
}
