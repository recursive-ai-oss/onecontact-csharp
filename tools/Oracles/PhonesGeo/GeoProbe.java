// Ad-hoc probe for the geocoder, carrier and time-zone mappers of a released libphonenumber release: reads lines of
// "country-code national-number language-tag [user-region]" and prints what the public API answers. It is only executed.
//   java -cp libphonenumber.jar:geocoder.jar:carrier.jar:prefixmapper.jar GeoProbe.java < lines
import com.google.i18n.phonenumbers.PhoneNumberToCarrierMapper;
import com.google.i18n.phonenumbers.PhoneNumberToTimeZonesMapper;
import com.google.i18n.phonenumbers.PhoneNumberUtil;
import com.google.i18n.phonenumbers.Phonenumber.PhoneNumber;
import com.google.i18n.phonenumbers.geocoding.PhoneNumberOfflineGeocoder;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.PrintStream;
import java.util.Locale;

public class GeoProbe {
  static String esc(String s) {
    if (s == null) return "<null>";
    StringBuilder sb = new StringBuilder();
    for (char c : s.toCharArray()) {
      if (c >= 0x20 && c <= 0x7e && c != '\\') sb.append(c); else sb.append(String.format("\\u%04X", (int) c));
    }
    return sb.toString();
  }

  public static void main(String[] args) throws Exception {
    PhoneNumberUtil util = PhoneNumberUtil.getInstance();
    PhoneNumberOfflineGeocoder geo = PhoneNumberOfflineGeocoder.getInstance();
    PhoneNumberToCarrierMapper carrier = PhoneNumberToCarrierMapper.getInstance();
    PhoneNumberToTimeZonesMapper tz = PhoneNumberToTimeZonesMapper.getInstance();
    PrintStream out = new PrintStream(System.out, false, "UTF-8");
    BufferedReader in = new BufferedReader(new InputStreamReader(System.in, "UTF-8"));
    String line;
    while ((line = in.readLine()) != null) {
      String[] f = line.split(" ");
      if (f[0].equals("@@tokens")) {
        // the calling codes that have a mobile token (a digit dialled before the area code of a mobile number)
        for (int cc = 1; cc < 1000; cc++) {
          String t = PhoneNumberUtil.getCountryMobileToken(cc);
          if (!t.isEmpty()) out.println("token " + cc + " " + t);
        }
        continue;
      }
      if (f[0].startsWith("@")) {
        // "@REGION language-tag": one line per number type, using the example number the metadata gives for the type
        for (PhoneNumberUtil.PhoneNumberType t : PhoneNumberUtil.PhoneNumberType.values()) {
          PhoneNumber e = util.getExampleNumberForType(f[0].substring(1), t);
          if (e == null) continue;
          Locale l = Locale.forLanguageTag(f[1]);
          out.println(f[0] + " " + t + " +" + e.getCountryCode() + " " + e.getNationalNumber() + " valid=" + util.isValidNumber(e)
              + " geo=" + esc(geo.getDescriptionForNumber(e, l)) + " geoOther=" + esc(geo.getDescriptionForNumber(e, l, "ZZ"))
              + " car=" + esc(carrier.getNameForNumber(e, l)) + " carS=" + esc(carrier.getSafeDisplayName(e, l))
              + " tz=" + tz.getTimeZonesForNumber(e) + " tzG=" + tz.getTimeZonesForGeographicalNumber(e));
        }
        continue;
      }
      PhoneNumber n = new PhoneNumber();
      n.setCountryCode(Integer.parseInt(f[0]));
      n.setNationalNumber(Long.parseLong(f[1]));
      Locale loc = Locale.forLanguageTag(f[2]);
      StringBuilder sb = new StringBuilder(line).append(" => valid=").append(util.isValidNumber(n)).append(" region=").append(util.getRegionCodeForNumber(n)).append(" type=").append(util.getNumberType(n));
      sb.append(" loc=").append(loc.getLanguage()).append('/').append(loc.getScript()).append('/').append(loc.getCountry());
      sb.append(" geo=").append(esc(geo.getDescriptionForNumber(n, loc)));
      if (f.length > 3) sb.append(" geoU=").append(esc(geo.getDescriptionForNumber(n, loc, f[3])));
      sb.append(" geoV=").append(esc(geo.getDescriptionForValidNumber(n, loc)));
      StringBuilder regs = new StringBuilder();
      for (String r : util.getRegionCodesForCountryCode(n.getCountryCode())) {
        regs.append(r).append(util.isValidNumberForRegion(n, r) ? "+" : "-").append(' ');
      }
      sb.append(" regions=").append(regs.toString().trim().replace(' ', ','));
      sb.append(" car=").append(esc(carrier.getNameForNumber(n, loc)));
      sb.append(" carV=").append(esc(carrier.getNameForValidNumber(n, loc)));
      sb.append(" carS=").append(esc(carrier.getSafeDisplayName(n, loc)));
      sb.append(" tz=").append(tz.getTimeZonesForNumber(n));
      sb.append(" tzG=").append(tz.getTimeZonesForGeographicalNumber(n));
      out.println(sb);
    }
    out.flush();
  }
}
