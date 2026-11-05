// Black-box probe: which inputs the reference calls alpha (vanity) numbers.
//   java -cp libphonenumber-<version>.jar AlphaProbe.java
import com.google.i18n.phonenumbers.PhoneNumberUtil;

public class AlphaProbe {
  public static void main(String[] args) {
    PhoneNumberUtil util = PhoneNumberUtil.getInstance();
    String[] inputs = {
      "tel:+12025550100;ext=204", "+12025550100;ext=204", "+12025550100 ext. 204", "+12025550100 extension 204", "202-555-0100 x204",
      "1-800-FLOWERS", "1-800-FLOWERS;ext=204", "tel:+1-800-FLOWERS;ext=204", "1-800-FLOWERS ext 5", "+12025550100;isub=abc", "+12025550100;phone-context=abc.example"
    };
    for (String s : inputs) {
      System.out.println(s + "\t" + util.isAlphaNumber(s));
    }
  }
}
